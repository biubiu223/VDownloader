using System.Reflection;

namespace VDownloader.Services;

public static class AppInfo
{
    public const string Name = "万能视频下载器";

    /// <summary>GitHub 仓库（用户名/仓库名）。项目主页、检查更新都用它。</summary>
    public const string Repo = "biubiu223/VDownloader";
    public const string RepoUrl = "https://github.com/" + Repo;
    public const string ReleasesUrl = RepoUrl + "/releases";

    public static System.Version VersionValue { get; } =
        Assembly.GetExecutingAssembly().GetName().Version ?? new System.Version(1, 0, 0);

    public static string Version { get; } = VersionValue.ToString(3);
}

/// <summary>程序用到的各种目录。用户数据放在 %LOCALAPPDATA%\VDownloader，程序目录保持只读也没关系。</summary>
public static class AppPaths
{
    public static string BaseDir { get; } = AppContext.BaseDirectory;

    public static string DataDir { get; } = Ensure(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VDownloader"));

    /// <summary>可写的组件目录（在线下载的 yt-dlp / ffmpeg / deno 放这里）。</summary>
    public static string UserToolsDir => Ensure(Path.Combine(DataDir, "tools"));

    /// <summary>随程序一起分发的组件目录（可选）。</summary>
    public static string BundledToolsDir => Path.Combine(BaseDir, "tools");

    public static string SettingsFile => Path.Combine(DataDir, "settings.json");
    public static string BiliCookieFile => Path.Combine(DataDir, "bilibili_cookies.txt");
    public static string LogFile => Path.Combine(DataDir, "app.log");
    public static string IconFile => Path.Combine(BaseDir, "Assets", "app.ico");

    public static string DefaultDownloadDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "视频下载");

    private static string Ensure(string dir)
    {
        try { Directory.CreateDirectory(dir); } catch { /* 交给使用处报错 */ }
        return dir;
    }
}
