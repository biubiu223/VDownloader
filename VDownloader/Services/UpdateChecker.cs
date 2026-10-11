using System.Net.Http;
using System.Text.Json;

namespace VDownloader.Services;

public sealed record ReleaseInfo(System.Version Version, string Tag, string Name, string Url, string Notes);

/// <summary>通过 GitHub Releases 检查软件新版本（只读查询，不会自动下载或安装）。</summary>
public static class UpdateChecker
{
    /// <summary>返回比当前版本新的发布；没有新版本时返回 null。网络错误会抛出异常。</summary>
    public static async Task<ReleaseInfo?> CheckAsync(CancellationToken ct = default)
    {
        using var http = Tools.CreateHttpClient();
        http.Timeout = TimeSpan.FromSeconds(15);
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        using var resp = await http.GetAsync($"https://api.github.com/repos/{AppInfo.Repo}/releases/latest", ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null; // 还没有发布过正式版本
        resp.EnsureSuccessStatusCode();

        var j = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct)).RootElement;
        var tag = j.GetProperty("tag_name").GetString() ?? "";
        if (!System.Version.TryParse(tag.TrimStart('v', 'V').Split('-', '+')[0], out var latest)) return null;
        var current = AppInfo.VersionValue;
        var cur3 = new System.Version(current.Major, current.Minor, Math.Max(current.Build, 0));
        var lat3 = new System.Version(latest.Major, latest.Minor, Math.Max(latest.Build, 0));
        if (lat3 <= cur3) return null;

        return new ReleaseInfo(lat3, tag,
            j.TryGetProperty("name", out var n) ? n.GetString() ?? tag : tag,
            j.TryGetProperty("html_url", out var u) ? u.GetString() ?? AppInfo.ReleasesUrl : AppInfo.ReleasesUrl,
            j.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "");
    }
}
