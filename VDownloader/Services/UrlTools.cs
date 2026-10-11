using System.Text.RegularExpressions;

namespace VDownloader.Services;

/// <summary>链接识别、站点判断和数值格式化。</summary>
public static partial class UrlTools
{
    private const string Stop = @"\s<>""'，。、；！？【】（）《》「」";

    [GeneratedRegex(@"https?://[^" + Stop + "]+", RegexOptions.IgnoreCase)]
    private static partial Regex UrlRe();

    [GeneratedRegex(@"(?<![\w/.@])((?:www\.|m\.)?(?:bilibili\.com|b23\.tv|youtube\.com|youtu\.be)/[^" + Stop + "]+)", RegexOptions.IgnoreCase)]
    private static partial Regex BareUrlRe();

    [GeneratedRegex(@"(?<![A-Za-z0-9])(BV1[0-9A-Za-z]{9})(?![A-Za-z0-9])")]
    private static partial Regex BvRe();

    /// <summary>从任意文本（B站分享文案、多行链接、BV 号……）中提取所有视频链接。</summary>
    public static List<string> ExtractUrls(string? text)
    {
        var found = new List<string>();
        if (string.IsNullOrWhiteSpace(text)) return found;
        static string Trim(string s) => s.TrimEnd('.', ',', ';', ':', '!', '?', ')', ']', '}', '>');

        foreach (Match m in UrlRe().Matches(text)) found.Add(Trim(m.Value));
        var rest = UrlRe().Replace(text, " ");
        foreach (Match m in BareUrlRe().Matches(rest)) found.Add("https://" + Trim(m.Groups[1].Value));
        rest = BareUrlRe().Replace(rest, " ");
        foreach (Match m in BvRe().Matches(rest)) found.Add($"https://www.bilibili.com/video/{m.Groups[1].Value}");
        return found.Distinct().ToList();
    }

    private static readonly string[] CnDomains =
    [
        "bilibili.com", "b23.tv", "bilivideo.com", "bili2233.cn", "acfun.cn", "douyin.com", "iesdouyin.com",
        "ixigua.com", "kuaishou.com", "youku.com", "iqiyi.com", "qq.com", "weibo.com", "weibo.cn",
        "xiaohongshu.com", "xhslink.com", "huya.com", "douyu.com", "zhihu.com", "mgtv.com", "cctv.com",
        "cntv.cn", "163.com", "miguvideo.com", "yy.com", "pearvideo.com",
    ];

    private static readonly string[] BiliDomains = ["bilibili.com", "b23.tv", "bili2233.cn", "bilivideo.com"];

    public static string Host(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) ? u.Host.ToLowerInvariant() : "";

    private static bool Match(string host, string[] domains) =>
        domains.Any(d => host == d || host.EndsWith("." + d, StringComparison.Ordinal));

    public static bool IsCnSite(string url) => Match(Host(url), CnDomains);
    public static bool IsBili(string url) => Match(Host(url), BiliDomains);

    /// <summary>站点显示名 + 徽标文字 + 徽标颜色。</summary>
    public static (string Name, string Badge, string Color) SiteInfo(string url, string? extractor = null)
    {
        var key = (extractor ?? "").ToLowerInvariant();
        var host = Host(url);
        bool Is(string k, params string[] hosts) => key.StartsWith(k) || hosts.Any(h => Match(host, [h]));

        if (Is("bilibili", "bilibili.com", "b23.tv", "bili2233.cn")) return ("B站", "B", "#FB7299");
        if (Is("youtube", "youtube.com", "youtu.be")) return ("YouTube", "▶", "#FF0033");
        if (Is("douyin", "douyin.com", "iesdouyin.com")) return ("抖音", "抖", "#1F1F1F");
        if (Is("tiktok", "tiktok.com")) return ("TikTok", "T", "#111111");
        if (Is("twitter", "twitter.com", "x.com")) return ("X", "X", "#0F1419");
        if (Is("instagram", "instagram.com")) return ("Instagram", "IG", "#D62976");
        if (Is("acfun", "acfun.cn")) return ("AcFun", "A", "#FD4C5D");
        if (Is("ixigua", "ixigua.com")) return ("西瓜视频", "西", "#F04142");
        if (Is("kuaishou", "kuaishou.com")) return ("快手", "快", "#FF4906");
        if (Is("weibo", "weibo.com", "weibo.cn")) return ("微博", "微", "#E6162D");
        if (Is("xiaohongshu", "xiaohongshu.com", "xhslink.com")) return ("小红书", "红", "#FF2442");
        if (Is("vimeo", "vimeo.com")) return ("Vimeo", "V", "#1AB7EA");
        if (Is("twitch", "twitch.tv")) return ("Twitch", "Tw", "#9146FF");
        if (Is("niconico", "nicovideo.jp")) return ("Niconico", "ニ", "#252525");
        if (Is("facebook", "facebook.com", "fb.watch")) return ("Facebook", "f", "#1877F2");
        if (Is("reddit", "reddit.com")) return ("Reddit", "R", "#FF4500");

        var name = !string.IsNullOrEmpty(extractor) && key != "generic" ? extractor! : host.Replace("www.", "");
        if (string.IsNullOrEmpty(name)) name = "网页";
        return (name, name[..1].ToUpperInvariant(), "#5B6CF0");
    }

    // ---------------- 格式化 ----------------
    public static string Size(double? bytes)
    {
        if (bytes is null or <= 0) return "";
        double n = bytes.Value;
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        int i = 0;
        while (n >= 1024 && i < units.Length - 1) { n /= 1024; i++; }
        return i <= 1 ? $"{n:0} {units[i]}" : $"{n:0.0} {units[i]}";
    }

    public static string Speed(double? bps) => bps is > 0 ? Size(bps) + "/s" : "";

    public static string Duration(double? seconds)
    {
        if (seconds is null or < 0) return "";
        var t = TimeSpan.FromSeconds(Math.Round(seconds.Value));
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes:00}:{t.Seconds:00}";
    }
}
