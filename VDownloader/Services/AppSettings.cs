using System.Text.Json;

namespace VDownloader.Services;

/// <summary>所有用户设置，自动保存为 JSON。</summary>
public sealed class AppSettings
{
    public const string DefaultOutTemplate = "%(title).100B [%(id)s].%(ext)s";

    public static AppSettings Current { get; private set; } = new();

    // ---- 下载 ----
    public string DownloadDir { get; set; } = AppPaths.DefaultDownloadDir;
    public string Quality { get; set; } = FormatPresets.Qualities[0];
    public string CodecPref { get; set; } = FormatPresets.CodecPrefs[0];
    public string Container { get; set; } = FormatPresets.Containers[0];
    public bool Playlist { get; set; }
    public bool Subtitles { get; set; }
    public bool EmbedThumbnail { get; set; }
    public int MaxConcurrent { get; set; } = 2;
    public string OutTemplate { get; set; } = DefaultOutTemplate;

    // ---- 网络 ----
    /// <summary>system / none / custom</summary>
    public string ProxyMode { get; set; } = "system";
    public string Proxy { get; set; } = "http://127.0.0.1:7890";
    public bool CnDirect { get; set; } = true;

    // ---- 其他网站 Cookie ----
    /// <summary>none / browser / file</summary>
    public string CookieMode { get; set; } = "none";
    public string CookieBrowser { get; set; } = "firefox";
    public string CookieFile { get; set; } = "";

    // ---- 外观与行为 ----
    /// <summary>system / light / dark</summary>
    public string Theme { get; set; } = "system";
    public bool WatchClipboard { get; set; } = true;
    public bool Notify { get; set; } = true;
    public bool CheckUpdates { get; set; } = true;
    public int WindowWidth { get; set; }
    public int WindowHeight { get; set; }

    // ---- 组件路径（留空 = 自动查找） ----
    public string YtDlpPath { get; set; } = "";
    public string FfmpegPath { get; set; } = "";

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };
    private static Timer? _saveTimer;

    public static void Load()
    {
        try
        {
            if (File.Exists(AppPaths.SettingsFile))
                Current = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(AppPaths.SettingsFile)) ?? new();
        }
        catch (Exception e)
        {
            Log.Warn("读取设置失败，已使用默认设置：" + e.Message);
            Current = new();
        }
        Current.Normalize();
    }

    private void Normalize()
    {
        if (!FormatPresets.Qualities.Contains(Quality)) Quality = FormatPresets.Qualities[0];
        if (!FormatPresets.CodecPrefs.Contains(CodecPref)) CodecPref = FormatPresets.CodecPrefs[0];
        if (!FormatPresets.Containers.Contains(Container)) Container = FormatPresets.Containers[0];
        MaxConcurrent = Math.Clamp(MaxConcurrent, 1, 5);
        if (string.IsNullOrWhiteSpace(DownloadDir)) DownloadDir = AppPaths.DefaultDownloadDir;
        if (string.IsNullOrWhiteSpace(OutTemplate)) OutTemplate = DefaultOutTemplate;
    }

    /// <summary>延迟 400ms 合并保存，避免频繁写盘。</summary>
    public static void Save()
    {
        _saveTimer?.Dispose();
        _saveTimer = new Timer(_ => SaveNow(), null, 400, Timeout.Infinite);
    }

    public static void SaveNow()
    {
        try
        {
            var tmp = AppPaths.SettingsFile + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(Current, JsonOpts));
            File.Move(tmp, AppPaths.SettingsFile, true);
        }
        catch (Exception e)
        {
            Log.Warn("保存设置失败：" + e.Message);
        }
    }
}
