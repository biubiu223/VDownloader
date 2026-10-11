using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using VDownloader.Services;

namespace VDownloader.Models;

public enum TaskState { Waiting, Parsing, Downloading, Processing, Completed, Failed, Cancelled }

/// <summary>一个下载任务。所有属性只在 UI 线程上修改，界面通过 x:Bind 自动刷新。</summary>
public sealed class DownloadTask : INotifyPropertyChanged
{
    private static int _nextId;

    public event PropertyChangedEventHandler? PropertyChanged;

    public int Id { get; } = Interlocked.Increment(ref _nextId);
    public string Url { get; }
    public DownloadOptions Options { get; }
    public DateTime CreatedAt { get; } = DateTime.Now;
    public CancellationTokenSource? Cts { get; set; }

    private readonly StringBuilder _log = new();
    private readonly object _logGate = new();

    public DownloadTask(string url, DownloadOptions options)
    {
        Url = url;
        Options = options;
        _title = url;
        ApplySite(null);
        _quality = options.CustomLabel ?? options.Quality;
    }

    // ------------------------------------------------------------ 站点徽标
    private string _site = "";
    public string Site { get => _site; private set => Set(ref _site, value, nameof(DetailText)); }

    private string _badge = "";
    public string Badge { get => _badge; private set => Set(ref _badge, value); }

    private SolidColorBrush _badgeBrush = new(Colors.SlateBlue);
    public SolidColorBrush BadgeBrush { get => _badgeBrush; private set => Set(ref _badgeBrush, value); }

    public void ApplySite(string? extractor)
    {
        var (name, badge, color) = UrlTools.SiteInfo(Url, extractor);
        Site = name;
        Badge = badge;
        BadgeBrush = new SolidColorBrush(ParseColor(color));
    }

    // ------------------------------------------------------------ 基本信息
    private string _title;
    public string Title { get => _title; set => Set(ref _title, value); }

    private string _quality;
    public string Quality { get => _quality; set => Set(ref _quality, value, nameof(DetailText)); }

    private string _sizeText = "";
    public string SizeText { get => _sizeText; set => Set(ref _sizeText, value, nameof(DetailText)); }

    private string _playlistText = "";
    public string PlaylistText { get => _playlistText; set => Set(ref _playlistText, value, nameof(StatusLine)); }

    public string DetailText => string.Join("  ·  ", new[] { Site, Quality, SizeText }.Where(s => !string.IsNullOrEmpty(s)));

    private string? _filePath;
    public string? FilePath { get => _filePath; set => Set(ref _filePath, value, nameof(OpenVisibility)); }

    private string? _error;
    public string? Error { get => _error; set => Set(ref _error, value, nameof(StatusLine)); }

    // ------------------------------------------------------------ 状态与进度
    private TaskState _state = TaskState.Waiting;
    public TaskState State
    {
        get => _state;
        set
        {
            if (!Set(ref _state, value)) return;
            Raise(nameof(IsActive), nameof(StatusLine), nameof(CancelVisibility), nameof(RetryVisibility),
                nameof(OpenVisibility), nameof(ShowError), nameof(ShowPaused), nameof(StatusBrush), nameof(StateGlyph));
        }
    }

    public bool IsActive => State is TaskState.Waiting or TaskState.Parsing or TaskState.Downloading or TaskState.Processing;
    public bool IsRunning => State is TaskState.Parsing or TaskState.Downloading or TaskState.Processing;

    private double _progress;
    public double Progress { get => _progress; set => Set(ref _progress, value, nameof(StatusLine)); }

    private bool _isIndeterminate;
    public bool IsIndeterminate { get => _isIndeterminate; set => Set(ref _isIndeterminate, value); }

    private string _stage = "";
    /// <summary>当前阶段的补充说明，例如“视频流”“正在合并音视频”。</summary>
    public string Stage { get => _stage; set => Set(ref _stage, value, nameof(StatusLine)); }

    private string _speed = "";
    public string Speed { get => _speed; set => Set(ref _speed, value, nameof(StatusLine)); }

    private string _eta = "";
    public string Eta { get => _eta; set => Set(ref _eta, value, nameof(StatusLine)); }

    public string StatusLine => State switch
    {
        TaskState.Waiting => "排队等待中",
        TaskState.Parsing => "正在解析视频信息…",
        TaskState.Downloading => Join("下载中", Stage, $"{Progress:0.0}%", PlaylistText, Speed, string.IsNullOrEmpty(Eta) ? "" : "剩余 " + Eta),
        TaskState.Processing => Join(Stage.Length > 0 ? Stage : "正在处理", PlaylistText),
        TaskState.Completed => Join("下载完成", Error ?? ""),
        TaskState.Failed => "失败：" + (Error ?? "未知错误"),
        TaskState.Cancelled => "已取消（再次开始会自动续传）",
        _ => "",
    };

    public bool ShowError => State == TaskState.Failed;
    public bool ShowPaused => State == TaskState.Cancelled;

    public Brush StatusBrush => State switch
    {
        TaskState.Failed => (Brush)Application.Current.Resources["DangerTextBrush"],
        TaskState.Completed => (Brush)Application.Current.Resources["SuccessTextBrush"],
        _ => (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
    };

    public string StateGlyph => State switch
    {
        TaskState.Completed => "",
        TaskState.Failed => "",
        TaskState.Cancelled => "",
        TaskState.Waiting => "",
        _ => "",
    };

    public Visibility CancelVisibility => IsActive ? Visibility.Visible : Visibility.Collapsed;
    public Visibility RetryVisibility => State is TaskState.Failed or TaskState.Cancelled ? Visibility.Visible : Visibility.Collapsed;
    public Visibility OpenVisibility => State == TaskState.Completed && File.Exists(FilePath) ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>重新开始前清空状态。</summary>
    public void Reset()
    {
        Error = null;
        Progress = 0;
        Speed = Eta = Stage = PlaylistText = "";
        IsIndeterminate = false;
        State = TaskState.Waiting;
    }

    // ------------------------------------------------------------ 任务日志（可在后台线程写）
    public void AppendLog(string line)
    {
        lock (_logGate)
        {
            if (_log.Length > 200_000) _log.Remove(0, 100_000);
            _log.AppendLine(line);
        }
    }

    public string GetLog()
    {
        lock (_logGate) return _log.ToString();
    }

    // ------------------------------------------------------------ 工具
    private static string Join(params string[] parts) =>
        string.Join("  ·  ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));

    private static Windows.UI.Color ParseColor(string hex)
    {
        hex = hex.TrimStart('#');
        return Windows.UI.Color.FromArgb(255, Convert.ToByte(hex[..2], 16), Convert.ToByte(hex[2..4], 16), Convert.ToByte(hex[4..6], 16));
    }

    private bool Set<T>(ref T field, T value, string? also = null, [CallerMemberName] string name = "")
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(name);
        if (also is not null) Raise(also);
        return true;
    }

    private void Raise(params string[] names)
    {
        foreach (var n in names) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }
}
