using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Text;

namespace VDownloader.Services;

/// <summary>一个外部组件（yt-dlp / ffmpeg / deno）的位置。</summary>
public sealed record ToolInfo(string? Path, string Source)
{
    public bool Found => Path is not null;
}

/// <summary>查找、更新、在线安装外部组件。</summary>
public static class Tools
{
    public static ToolInfo YtDlp { get; private set; } = new(null, "");
    public static ToolInfo Ffmpeg { get; private set; } = new(null, "");
    public static ToolInfo Deno { get; private set; } = new(null, "");

    /// <summary>yt-dlp 来自 Python 虚拟环境时，对应的 python.exe（用于 pip 升级）。</summary>
    public static string? VenvPython { get; private set; }

    static Tools() => Refresh();

    public static void Refresh()
    {
        var s = AppSettings.Current;
        var venvScripts = FindVenvScripts();
        VenvPython = null;

        YtDlp = FirstFound(
            (s.YtDlpPath, "自定义"),
            (Path.Combine(AppPaths.UserToolsDir, "yt-dlp.exe"), "在线组件"),
            (Path.Combine(AppPaths.BundledToolsDir, "yt-dlp.exe"), "随程序附带"),
            (venvScripts is null ? null : Path.Combine(venvScripts, "yt-dlp.exe"), "Python 环境"),
            (SearchPath("yt-dlp.exe"), "系统 PATH"));
        if (YtDlp.Source == "Python 环境")
            VenvPython = Path.Combine(venvScripts!, "python.exe");

        Ffmpeg = FirstFound(
            (s.FfmpegPath, "自定义"),
            (Path.Combine(AppPaths.UserToolsDir, "ffmpeg.exe"), "在线组件"),
            (Path.Combine(AppPaths.BundledToolsDir, "ffmpeg.exe"), "随程序附带"),
            (SearchPath("ffmpeg.exe"), "系统 PATH"),
            (FindImageioFfmpeg(venvScripts), "Python 环境"));

        Deno = FirstFound(
            (Path.Combine(AppPaths.UserToolsDir, "deno.exe"), "在线组件"),
            (Path.Combine(AppPaths.BundledToolsDir, "deno.exe"), "随程序附带"),
            (venvScripts is null ? null : Path.Combine(venvScripts, "deno.exe"), "Python 环境"),
            (SearchPath("deno.exe"), "系统 PATH"));
    }

    private static ToolInfo FirstFound(params (string? Path, string Source)[] candidates)
    {
        foreach (var (p, src) in candidates)
            if (!string.IsNullOrWhiteSpace(p) && File.Exists(p)) return new ToolInfo(p, src);
        return new ToolInfo(null, "未找到");
    }

    /// <summary>从程序目录往上找 .venv\Scripts（开发环境 / 和 Python 版共用内核时）。</summary>
    private static string? FindVenvScripts()
    {
        var dir = new DirectoryInfo(AppPaths.BaseDir);
        for (int i = 0; i < 8 && dir is not null; i++, dir = dir.Parent)
        {
            var scripts = Path.Combine(dir.FullName, ".venv", "Scripts");
            if (File.Exists(Path.Combine(scripts, "yt-dlp.exe")) || File.Exists(Path.Combine(scripts, "python.exe")))
                return scripts;
        }
        return null;
    }

    private static string? FindImageioFfmpeg(string? venvScripts)
    {
        if (venvScripts is null) return null;
        var bin = Path.Combine(Path.GetDirectoryName(venvScripts)!, "Lib", "site-packages", "imageio_ffmpeg", "binaries");
        return Directory.Exists(bin) ? Directory.GetFiles(bin, "ffmpeg*.exe").FirstOrDefault() : null;
    }

    private static string? SearchPath(string exe)
    {
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var p = Path.Combine(dir.Trim().Trim('"'), exe);
                if (File.Exists(p)) return p;
            }
            catch { }
        }
        return null;
    }

    // ------------------------------------------------------------------ 进程辅助
    public static ProcessStartInfo CreateStartInfo(string exe, IEnumerable<string> args)
    {
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
            WorkingDirectory = AppPaths.DataDir,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        psi.Environment["PYTHONIOENCODING"] = "utf-8";
        psi.Environment["PYTHONUTF8"] = "1";
        return psi;
    }

    /// <summary>运行一个命令，逐行回调输出，返回退出码。取消时结束整个进程树。</summary>
    public static async Task<int> RunAsync(string exe, IEnumerable<string> args, Action<string>? onLine,
        CancellationToken ct = default)
    {
        using var p = new Process { StartInfo = CreateStartInfo(exe, args) };
        p.OutputDataReceived += (_, e) => { if (e.Data is not null) onLine?.Invoke(e.Data); };
        p.ErrorDataReceived += (_, e) => { if (e.Data is not null) onLine?.Invoke(e.Data); };
        p.Start();
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        using var reg = ct.Register(() => { try { p.Kill(true); } catch { } });
        await p.WaitForExitAsync(CancellationToken.None);
        ct.ThrowIfCancellationRequested();
        return p.ExitCode;
    }

    public static async Task<string?> GetVersionAsync(ToolInfo tool, string arg = "--version")
    {
        if (!tool.Found) return null;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            string? first = null;
            await RunAsync(tool.Path!, [arg], line => first ??= line.Trim(), cts.Token);
            if (first is null) return null;
            // ffmpeg 输出 "ffmpeg version 7.1-xxx Copyright..."，deno 输出 "deno 2.x.x (...)"
            if (first.StartsWith("ffmpeg version ")) return first.Split(' ')[2];
            if (first.StartsWith("deno ")) return first.Split(' ')[1];
            return first;
        }
        catch { return null; }
    }

    // ------------------------------------------------------------------ 更新 / 安装
    /// <summary>更新 yt-dlp：Python 环境用 pip 升级；独立 exe 用自带的 -U。</summary>
    public static async Task<bool> UpdateYtDlpAsync(Action<string> log, CancellationToken ct = default)
    {
        if (!YtDlp.Found)
        {
            await InstallAsync("yt-dlp", log, null, ct);
            return true;
        }
        if (VenvPython is not null && File.Exists(VenvPython))
        {
            string[] pkgs = ["-m", "pip", "install", "-U", "--disable-pip-version-check", "yt-dlp[default]", "yt-dlp-ejs"];
            log("使用 pip 升级 yt-dlp …");
            var code = await RunAsync(VenvPython, pkgs, log, ct);
            if (code != 0)
            {
                log("官方源失败，改用清华镜像重试 …");
                code = await RunAsync(VenvPython, [.. pkgs, "-i", "https://pypi.tuna.tsinghua.edu.cn/simple"], log, ct);
            }
            return code == 0;
        }
        var args = new List<string> { "-U" };
        var proxy = AppSettings.Current.ProxyMode == "custom" ? AppSettings.Current.Proxy : null;
        if (!string.IsNullOrWhiteSpace(proxy)) args.AddRange(["--proxy", proxy!]);
        log("使用 yt-dlp -U 自我更新 …");
        if (await RunAsync(YtDlp.Path!, args, log, ct) == 0) return true;

        // 程序放在只读目录（比如 Program Files）时无法覆盖自身，改为下载新版到用户目录（优先级更高）
        if (YtDlp.Source != "在线组件")
        {
            log("原地更新失败，改为下载最新版到用户目录 …");
            await InstallAsync("yt-dlp", log, null, ct);
            return true;
        }
        return false;
    }

    private static readonly Dictionary<string, (string Url, string Desc)> Sources = new()
    {
        ["yt-dlp"] = ("https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe", "yt-dlp（约 18 MB）"),
        ["ffmpeg"] = ("https://github.com/yt-dlp/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip", "FFmpeg（约 140 MB）"),
        ["deno"] = ("https://github.com/denoland/deno/releases/latest/download/deno-x86_64-pc-windows-msvc.zip", "Deno（约 45 MB）"),
    };

    public static string Describe(string tool) => Sources[tool].Desc;

    public static HttpClient CreateHttpClient(bool forceDirect = false)
    {
        var s = AppSettings.Current;
        var handler = new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All };
        if (forceDirect || s.ProxyMode == "none") handler.UseProxy = false;
        else if (s.ProxyMode == "custom" && Uri.TryCreate(s.Proxy, UriKind.Absolute, out var proxy))
            handler.Proxy = new WebProxy(proxy);
        var http = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"VDownloader/{AppInfo.Version}");
        return http;
    }

    /// <summary>从官方 GitHub 发布页下载组件到 %LOCALAPPDATA%\VDownloader\tools。</summary>
    public static async Task InstallAsync(string tool, Action<string> log, IProgress<double>? progress,
        CancellationToken ct = default)
    {
        var (url, desc) = Sources[tool];
        var dir = AppPaths.UserToolsDir;
        var tmp = Path.Combine(dir, $"{tool}.download");
        log($"开始下载 {desc}：{url}");

        using (var http = CreateHttpClient())
        using (var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            resp.EnsureSuccessStatusCode();
            var total = resp.Content.Headers.ContentLength;
            await using var src = await resp.Content.ReadAsStreamAsync(ct);
            await using var dst = File.Create(tmp);
            var buf = new byte[1 << 16];
            long done = 0;
            int n;
            while ((n = await src.ReadAsync(buf, ct)) > 0)
            {
                await dst.WriteAsync(buf.AsMemory(0, n), ct);
                done += n;
                if (total > 0) progress?.Report(done * 100.0 / total.Value);
            }
        }

        if (url.EndsWith(".zip"))
        {
            log("下载完成，正在解压 …");
            using var zip = ZipFile.OpenRead(tmp);
            string[] wanted = tool == "ffmpeg" ? ["ffmpeg.exe", "ffprobe.exe"] : [$"{tool}.exe"];
            foreach (var entry in zip.Entries.Where(e => wanted.Contains(e.Name, StringComparer.OrdinalIgnoreCase)))
                entry.ExtractToFile(Path.Combine(dir, entry.Name.ToLowerInvariant()), overwrite: true);
            zip.Dispose();
            File.Delete(tmp);
        }
        else
        {
            File.Move(tmp, Path.Combine(dir, $"{tool}.exe"), overwrite: true);
        }
        log($"{desc} 安装完成。");
        Refresh();
    }
}
