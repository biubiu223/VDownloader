using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.UI.Dispatching;
using VDownloader.Models;

namespace VDownloader.Services;

/// <summary>下载队列：控制同时下载数量，负责启动、取消、重试任务。只在 UI 线程上调用。</summary>
public sealed class DownloadManager
{
    public static DownloadManager Instance { get; } = new();

    /// <summary>全部任务，最新的在最前面。</summary>
    public ObservableCollection<DownloadTask> Tasks { get; } = [];

    /// <summary>首页“最近任务”（最多 6 个）。</summary>
    public ObservableCollection<DownloadTask> Recent { get; } = [];

    public event Action? CountsChanged;
    public event Action<DownloadTask>? TaskFinished;

    private DispatcherQueue _dq = null!;

    public void Initialize(DispatcherQueue dq) => _dq = dq;

    public int ActiveCount => Tasks.Count(t => t.IsActive);

    public DownloadTask Add(string url, DownloadOptions options)
    {
        var t = new DownloadTask(url, options);
        Tasks.Insert(0, t);
        Recent.Insert(0, t);
        while (Recent.Count > 6) Recent.RemoveAt(Recent.Count - 1);
        Log.Info($"新任务 #{t.Id}：{url}（{options.CustomLabel ?? options.Quality}）");
        Schedule();
        return t;
    }

    public void Cancel(DownloadTask t)
    {
        if (t.State == TaskState.Waiting) t.State = TaskState.Cancelled;
        else t.Cts?.Cancel();
        CountsChanged?.Invoke();
        Schedule();
    }

    public void CancelAll()
    {
        foreach (var t in Tasks.Where(t => t.IsActive).ToList()) Cancel(t);
    }

    public void Retry(DownloadTask t)
    {
        if (t.IsActive) return;
        t.Reset();
        Schedule();
    }

    public void Remove(DownloadTask t)
    {
        if (t.IsActive) Cancel(t);
        Tasks.Remove(t);
        Recent.Remove(t);
        CountsChanged?.Invoke();
    }

    public void ClearFinished()
    {
        foreach (var t in Tasks.Where(t => !t.IsActive).ToList())
        {
            Tasks.Remove(t);
            Recent.Remove(t);
        }
        CountsChanged?.Invoke();
    }

    /// <summary>按设置的并发数启动排队中的任务。</summary>
    public void Schedule()
    {
        int running = Tasks.Count(t => t.IsRunning);
        foreach (var t in Tasks.Reverse().Where(t => t.State == TaskState.Waiting).ToList())
        {
            if (running >= AppSettings.Current.MaxConcurrent) break;
            Start(t);
            running++;
        }
        CountsChanged?.Invoke();
    }

    private async void Start(DownloadTask t)
    {
        t.State = TaskState.Parsing;
        t.IsIndeterminate = true;
        t.Cts = new CancellationTokenSource();
        var runner = new TaskRunner(t, _dq);
        (TaskState state, string? error) result;
        try
        {
            result = await Task.Run(() => runner.RunAsync(t.Cts.Token));
        }
        catch (Exception e)
        {
            result = (TaskState.Failed, e.Message);
        }

        t.IsIndeterminate = false;
        t.Speed = t.Eta = "";
        t.Error = result.error;
        if (result.state == TaskState.Completed) { t.Progress = 100; t.Stage = ""; }
        t.State = result.state;
        Log.Write(result.state == TaskState.Failed ? "错误" : "信息",
            $"任务 #{t.Id} {t.StatusLine}  {t.Title}");
        TaskFinished?.Invoke(t);
        Schedule();
    }
}

/// <summary>在后台线程运行一个 yt-dlp 进程，并把它的输出翻译成任务进度。</summary>
internal sealed class TaskRunner(DownloadTask task, DispatcherQueue dq)
{
    // yt-dlp 输出模板。字段用制表符分隔，每个字段都是 JSON（缺失时为空）。
    private const string InfoTemplate =
        "before_dl:[INFO]%(title|)j\t%(extractor_key|)j\t%(height|)j\t%(fps|)j\t%(vcodec|)j\t%(acodec|)j\t" +
        "%(dynamic_range|)j\t%(filesize,filesize_approx|)j\t%(playlist_index|)j\t%(n_entries|)j\t" +
        "%(requested_formats.:.{format_id,filesize,filesize_approx,quality}|)j\t%(quality|)j\t%(format_note|)j\t%(abr|)j";

    private const string ProgressTemplate =
        "download:[P]%(progress.{status,downloaded_bytes,total_bytes,total_bytes_estimate,speed,eta})j\t" +
        "%(info.{format_id,vcodec,acodec,playlist_index,n_entries})j";

    private const string PostTemplate = "postprocess:[PP]%(progress.{status,postprocessor})j";

    private readonly object _gate = new();
    private readonly List<string> _errors = [];
    private readonly Queue<string> _tail = new();
    private List<(string Id, double Size)> _parts = [];
    private long _lastUiTick;
    private int _filesDone;
    private bool _isList;

    public async Task<(TaskState, string?)> RunAsync(CancellationToken ct)
    {
        var o = task.Options;
        string? tempCookie = null;
        try
        {
            if (!Tools.YtDlp.Found)
                return (TaskState.Failed, "没有找到 yt-dlp 解析内核，请到「设置 → 组件」中安装。");
            Directory.CreateDirectory(o.DownloadDir);

            var outTmpl = AppSettings.Current.OutTemplate;
            if (o.Playlist)
            {
                // 先快速看一下是不是合集；是的话放进以合集命名的子文件夹
                using var doc = await YtDlp.ProbeAsync(task.Url, true, ct);
                var pr = ProbeResult.Parse(task.Url, doc);
                if (pr.IsPlaylist)
                {
                    _isList = true;
                    outTmpl = "%(playlist_title).80B/%(playlist_index)03d - " + outTmpl;
                    Ui(() =>
                    {
                        task.Title = pr.Title;
                        task.PlaylistText = $"共 {pr.EntryCount} 个";
                    });
                }
            }

            var args = YtDlp.CommonArgs(task.Url, o.Playlist, out tempCookie);
            args.AddRange([
                "--quiet", "--progress", "--no-simulate", "--no-mtime", "--concurrent-fragments", "4",
                "--progress-template", ProgressTemplate,
                "--progress-template", PostTemplate,
                "--print", InfoTemplate,
                "--print", "after_move:[FILE]%(filepath)j",
                "-P", "home:" + o.DownloadDir,
                "-o", outTmpl,
            ]);
            args.AddRange(FormatPresets.BuildArgs(o));
            if (o.Subtitles) args.AddRange(["--write-subs", "--sub-langs", "all,-live_chat"]);
            if (o.EmbedThumbnail) args.Add("--embed-thumbnail");
            if (_isList) args.Add("--ignore-errors");
            args.AddRange(["--", task.Url]);

            task.AppendLog("> yt-dlp " + string.Join(' ', args.Select(a => a.Contains(' ') ? $"\"{a}\"" : a)));
            var code = await Tools.RunAsync(Tools.YtDlp.Path!, args, OnLine, ct);

            if (code == 0) return (TaskState.Completed, null);
            if (_isList && _filesDone > 0)
                return (TaskState.Completed, $"其中 {_errors.Count} 个视频下载失败，可在日志中查看原因");
            lock (_gate)
                return (TaskState.Failed, ErrorHints.Explain(string.Join("\n", _errors.Count > 0 ? _errors : _tail)));
        }
        catch (OperationCanceledException)
        {
            return (TaskState.Cancelled, null);
        }
        catch (Exception e)
        {
            return (TaskState.Failed, e.Message);
        }
        finally
        {
            YtDlp.DeleteQuietly(tempCookie);
        }
    }

    private void Ui(Action a) => dq.TryEnqueue(() => a());

    // ------------------------------------------------------------------ 输出解析
    private void OnLine(string line)
    {
        lock (_gate)
        {
            try { Handle(line); }
            catch (Exception e) { task.AppendLog("[解析输出出错] " + e.Message); }
        }
    }

    private void Handle(string line)
    {
        if (line.StartsWith("[P]")) { OnProgress(line[3..]); return; }

        task.AppendLog(line);
        if (line.StartsWith("[INFO]")) OnInfo(line[6..]);
        else if (line.StartsWith("[PP]")) OnPost(line[4..]);
        else if (line.StartsWith("[FILE]")) OnFile(line[6..]);
        else if (line.StartsWith("ERROR:"))
        {
            _errors.Add(line);
            Log.Error($"任务 #{task.Id}：{line}");
        }
        else if (line.StartsWith("WARNING:"))
        {
            Log.Warn($"任务 #{task.Id}：{line[8..].Trim()}");
        }
        else
        {
            _tail.Enqueue(line);
            if (_tail.Count > 20) _tail.Dequeue();
        }
    }

    private static JsonElement? Field(string[] parts, int i)
    {
        if (i >= parts.Length || string.IsNullOrWhiteSpace(parts[i])) return null;
        try { return JsonDocument.Parse(parts[i]).RootElement.Clone(); }
        catch { return null; }
    }

    private static string? S(JsonElement? e) => e is { ValueKind: JsonValueKind.String } v ? v.GetString() : null;
    private static double? N(JsonElement? e) => e is { ValueKind: JsonValueKind.Number } v ? v.GetDouble() : null;

    /// <summary>每个视频开始下载前：标题、实际画质、各分片大小。</summary>
    private void OnInfo(string payload)
    {
        var p = payload.Split('\t');
        var title = S(Field(p, 0));
        var extractor = S(Field(p, 1));
        var height = N(Field(p, 2));
        var fps = N(Field(p, 3));
        var vcodec = S(Field(p, 4));
        var acodec = S(Field(p, 5));
        var hdr = S(Field(p, 6));
        var size = N(Field(p, 7));
        var plIndex = N(Field(p, 8));
        var plCount = N(Field(p, 9));
        var req = Field(p, 10);
        var quality = N(Field(p, 11));
        var abr = N(Field(p, 13));

        _parts = [];
        double? biliQn = quality;
        if (req is { ValueKind: JsonValueKind.Array } arr)
        {
            foreach (var f in arr.EnumerateArray())
            {
                var id = ProbeResult.Str(f, "format_id") ?? "";
                var sz = ProbeResult.Num(f, "filesize") ?? ProbeResult.Num(f, "filesize_approx") ?? 0;
                _parts.Add((id, sz));
                biliQn ??= ProbeResult.Num(f, "quality");
            }
            if (_parts.All(x => x.Size > 0)) size = _parts.Sum(x => x.Size);
        }

        // 组合出“实际画质”文字，例如：4K 超清 · HEVC · HDR10 / 2160p60 · AV1
        string label;
        bool audioOnly = vcodec is null or "none";
        if (audioOnly)
        {
            label = string.Join(" · ", new[] { "音频", ProbeResult.ACodec(acodec), abr is > 0 ? $"{abr:0} kbps" : "" }
                .Where(s => s.Length > 0));
        }
        else
        {
            var isBili = (extractor ?? "").StartsWith("BiliBili", StringComparison.OrdinalIgnoreCase);
            string res = isBili && biliQn is not null && ProbeResult.BiliQn.TryGetValue((int)biliQn, out var qn)
                ? qn
                : height is > 0 ? $"{height:0}p{(fps is > 30 ? $"{fps:0}" : "")}" : "";
            var hdrText = hdr is null or "SDR" ? "" : hdr == "DV" ? "杜比视界" : hdr;
            label = string.Join(" · ", new[] { res, ProbeResult.VCodec(vcodec), hdrText }.Where(s => s.Length > 0));
        }

        Ui(() =>
        {
            if (!task.IsRunning) return;
            if (!_isList && !string.IsNullOrEmpty(title)) task.Title = title;
            task.ApplySite(extractor);
            if (label.Length > 0) task.Quality = label;
            task.SizeText = UrlTools.Size(size);
            if (plIndex is > 0 && plCount is > 0) task.PlaylistText = $"第 {plIndex:0}/{plCount:0} 个";
            task.State = TaskState.Downloading;
            task.IsIndeterminate = false;
        });
    }

    private void OnProgress(string payload)
    {
        var p = payload.Split('\t');
        var prog = Field(p, 0);
        var info = Field(p, 1);
        if (prog is null) return;
        var pr = prog.Value;
        if (ProbeResult.Str(pr, "status") != "downloading") return;

        // 限制刷新频率：每 150ms 最多更新一次界面
        var now = Stopwatch.GetTimestamp();
        if (Stopwatch.GetElapsedTime(_lastUiTick, now).TotalMilliseconds < 150) return;
        _lastUiTick = now;

        var done = ProbeResult.Num(pr, "downloaded_bytes") ?? 0;
        var total = ProbeResult.Num(pr, "total_bytes") ?? ProbeResult.Num(pr, "total_bytes_estimate");
        var speed = ProbeResult.Num(pr, "speed");
        var eta = ProbeResult.Num(pr, "eta");
        double? frac = total is > 0 ? Math.Clamp(done / total.Value, 0, 1) : null;

        string fid = "", vcodec = "", acodec = "";
        double? plIndex = null, plCount = null;
        if (info is { ValueKind: JsonValueKind.Object } inf)
        {
            fid = ProbeResult.Str(inf, "format_id") ?? "";
            vcodec = ProbeResult.Str(inf, "vcodec") ?? "";
            acodec = ProbeResult.Str(inf, "acodec") ?? "";
            plIndex = ProbeResult.Num(inf, "playlist_index");
            plCount = ProbeResult.Num(inf, "n_entries");
        }

        // 视频流 + 音频流分开下载时，按大小加权算总进度
        double item = frac ?? 0;
        string stage = "";
        int idx = _parts.FindIndex(x => x.Id == fid);
        if (_parts.Count > 1 && idx >= 0)
        {
            var weights = _parts.All(x => x.Size > 0) ? _parts.Select(x => x.Size).ToList() : _parts.Select(_ => 1.0).ToList();
            item = (weights.Take(idx).Sum() + weights[idx] * (frac ?? 0)) / weights.Sum();
            stage = vcodec == "none" ? "音频流" : "视频流";
        }
        double overall = plIndex is > 0 && plCount is > 0 ? ((plIndex.Value - 1) + item) / plCount.Value : item;

        Ui(() =>
        {
            if (!task.IsRunning) return;
            task.State = TaskState.Downloading;
            task.IsIndeterminate = frac is null;
            task.Progress = Math.Round(overall * 100, 1);
            task.Stage = stage;
            task.Speed = UrlTools.Speed(speed);
            task.Eta = eta is >= 0 ? UrlTools.Duration(eta) : "";
            if (string.IsNullOrEmpty(task.SizeText) && total is > 0 && _parts.Count <= 1) task.SizeText = UrlTools.Size(total);
        });
    }

    private void OnPost(string payload)
    {
        var e = Field([payload], 0);
        if (e is null) return;
        var status = ProbeResult.Str(e.Value, "status");
        var pp = ProbeResult.Str(e.Value, "postprocessor") ?? "";
        if (status != "started" || pp is "MoveFiles") return;
        var text = pp switch
        {
            "Merger" => "正在合并音视频",
            "ExtractAudio" => "正在转换音频",
            "EmbedThumbnail" => "正在嵌入封面",
            "EmbedSubtitle" => "正在嵌入字幕",
            "VideoConvertor" or "VideoRemuxer" => "正在转换格式",
            _ when pp.StartsWith("Fixup") => "正在修复文件",
            _ => "正在处理",
        };
        Ui(() =>
        {
            if (!task.IsRunning) return;
            task.State = TaskState.Processing;
            task.IsIndeterminate = true;
            task.Stage = text;
            task.Speed = task.Eta = "";
        });
    }

    private void OnFile(string payload)
    {
        var path = S(Field([payload], 0));
        if (string.IsNullOrEmpty(path)) return;
        _filesDone++;
        Ui(() => task.FilePath = path);
    }
}
