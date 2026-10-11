using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace VDownloader.Services;

public sealed record BiliStatus(bool? LoggedIn, string Name, string Face, bool Vip, string VipLabel, int Level, string Error)
{
    public static readonly BiliStatus NotLoggedIn = new(false, "", "", false, "", 0, "");
}

/// <summary>
/// B站网页端官方扫码登录。登录后 Cookie 以 Netscape 格式保存，供 yt-dlp 下载高画质使用。
/// 全程不接触账号密码。
/// </summary>
public static class BiliService
{
    private const string UA =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";

    public const int QrSuccess = 0, QrExpired = 86038, QrScanned = 86090, QrWaiting = 86101;

    public static BiliStatus Status { get; private set; } = BiliStatus.NotLoggedIn;
    public static event Action? StatusChanged;

    private static HttpClient CreateClient(CookieContainer jar)
    {
        var s = AppSettings.Current;
        var handler = new HttpClientHandler { CookieContainer = jar, AutomaticDecompression = DecompressionMethods.All };
        if (s.CnDirect || s.ProxyMode == "none") handler.UseProxy = false;
        else if (s.ProxyMode == "custom" && Uri.TryCreate(s.Proxy, UriKind.Absolute, out var p)) handler.Proxy = new WebProxy(p);
        var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(UA);
        http.DefaultRequestHeaders.Referrer = new Uri("https://www.bilibili.com/");
        return http;
    }

    private static async Task<JsonElement> GetJsonAsync(HttpClient http, string url, CancellationToken ct)
    {
        var text = await http.GetStringAsync(url, ct);
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    /// <summary>一次扫码登录会话。</summary>
    public sealed class QrSession : IDisposable
    {
        private readonly CookieContainer _jar = new();
        private readonly HttpClient _http;
        private string _key = "";

        public QrSession() => _http = CreateClient(_jar);

        /// <summary>返回二维码里要编码的网址。</summary>
        public async Task<string> GenerateAsync(CancellationToken ct = default)
        {
            var j = await GetJsonAsync(_http, "https://passport.bilibili.com/x/passport-login/web/qrcode/generate", ct);
            if (j.GetProperty("code").GetInt32() != 0) throw new InvalidOperationException(j.GetProperty("message").GetString());
            var d = j.GetProperty("data");
            _key = d.GetProperty("qrcode_key").GetString()!;
            return d.GetProperty("url").GetString()!;
        }

        /// <summary>查询扫码状态；成功时自动保存 Cookie。</summary>
        public async Task<int> PollAsync(CancellationToken ct = default)
        {
            var j = await GetJsonAsync(_http,
                "https://passport.bilibili.com/x/passport-login/web/qrcode/poll?qrcode_key=" + Uri.EscapeDataString(_key), ct);
            var d = j.GetProperty("data");
            var code = d.GetProperty("code").GetInt32();
            if (code == QrSuccess)
            {
                AddCookiesFromUrl(d.TryGetProperty("url", out var u) ? u.GetString() : null);
                SaveNetscape(_jar, AppPaths.BiliCookieFile);
                Log.Info("B站扫码登录成功，Cookie 已保存。");
            }
            return code;
        }

        /// <summary>正常情况下 Set-Cookie 已写入；再从回调地址补一遍，防止个别情况下丢失。</summary>
        private void AddCookiesFromUrl(string? url)
        {
            if (url is null || !Uri.TryCreate(url, UriKind.Absolute, out var uri)) return;
            var have = _jar.GetAllCookies().Select(c => c.Name).ToHashSet();
            var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
            var expires = DateTime.UtcNow.AddDays(180);
            if (long.TryParse(query["Expires"], out var exp)) expires = DateTimeOffset.FromUnixTimeSeconds(exp).UtcDateTime;
            foreach (var name in new[] { "SESSDATA", "bili_jct", "DedeUserID", "DedeUserID__ckMd5" })
            {
                var value = query[name];
                if (value is null || have.Contains(name)) continue;
                _jar.Add(new Cookie(name, value, "/", ".bilibili.com") { Expires = expires });
            }
        }

        public void Dispose() => _http.Dispose();
    }

    private static void SaveNetscape(CookieContainer jar, string path)
    {
        var sb = new StringBuilder("# Netscape HTTP Cookie File\n# 由 万能视频下载器 生成，请勿分享给他人\n\n");
        foreach (Cookie c in jar.GetAllCookies())
        {
            var domain = c.Domain.StartsWith('.') ? c.Domain : "." + c.Domain;
            long expires = c.Expires == DateTime.MinValue
                ? DateTimeOffset.UtcNow.AddDays(180).ToUnixTimeSeconds()
                : new DateTimeOffset(c.Expires.ToUniversalTime()).ToUnixTimeSeconds();
            sb.Append($"{domain}\tTRUE\t{c.Path}\t{(c.Secure ? "TRUE" : "FALSE")}\t{expires}\t{c.Name}\t{c.Value}\n");
        }
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
    }

    private static CookieContainer LoadNetscape(string path)
    {
        var jar = new CookieContainer();
        foreach (var line in File.ReadAllLines(path))
        {
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var f = line.Split('\t');
            if (f.Length < 7) continue;
            try { jar.Add(new Cookie(f[5], f[6], f[2], f[0])); } catch { }
        }
        return jar;
    }

    /// <summary>查询登录状态（用户名、头像、大会员）。</summary>
    public static async Task<BiliStatus> RefreshStatusAsync(CancellationToken ct = default)
    {
        BiliStatus result;
        if (!File.Exists(AppPaths.BiliCookieFile))
        {
            result = BiliStatus.NotLoggedIn;
        }
        else
        {
            try
            {
                using var http = CreateClient(LoadNetscape(AppPaths.BiliCookieFile));
                var j = await GetJsonAsync(http, "https://api.bilibili.com/x/web-interface/nav", ct);
                var d = j.TryGetProperty("data", out var dd) ? dd : default;
                if (j.GetProperty("code").GetInt32() == 0 && d.ValueKind == JsonValueKind.Object
                    && d.TryGetProperty("isLogin", out var il) && il.GetBoolean())
                {
                    bool vip = d.TryGetProperty("vipStatus", out var vs) && vs.GetInt32() == 1;
                    string label = "";
                    if (d.TryGetProperty("vip_label", out var vl) && vl.TryGetProperty("text", out var vt)) label = vt.GetString() ?? "";
                    int level = d.TryGetProperty("level_info", out var li) && li.TryGetProperty("current_level", out var cl) ? cl.GetInt32() : 0;
                    result = new BiliStatus(true, d.GetProperty("uname").GetString() ?? "",
                        d.TryGetProperty("face", out var face) ? face.GetString() ?? "" : "",
                        vip, vip ? (label.Length > 0 ? label : "大会员") : "", level, "");
                }
                else
                {
                    result = BiliStatus.NotLoggedIn with { Error = "登录已失效，请重新扫码" };
                }
            }
            catch (Exception e)
            {
                result = new BiliStatus(null, "", "", false, "", 0, "网络错误：" + e.Message);
            }
        }
        Status = result;
        StatusChanged?.Invoke();
        return result;
    }

    /// <summary>
    /// 退出登录：先请求 B站官方接口让这次登录在服务器端失效（即使凭据被人拷走也无法再用），
    /// 然后无论服务器是否响应成功，都删除本机保存的 Cookie。返回给用户看的结果说明。
    /// </summary>
    public static async Task<(bool ServerRevoked, string Message)> LogoutAsync(CancellationToken ct = default)
    {
        bool revoked = false;
        string message = "本机没有保存登录信息。";
        var path = AppPaths.BiliCookieFile;
        try
        {
            if (File.Exists(path))
            {
                var jar = LoadNetscape(path);
                var csrf = jar.GetAllCookies().FirstOrDefault(c => c.Name == "bili_jct")?.Value;
                if (string.IsNullOrEmpty(csrf))
                {
                    message = "登录信息里缺少 bili_jct，无法通知 B站服务器，只删除了本机的登录信息。";
                }
                else
                {
                    (revoked, message) = await RevokeOnServerAsync(jar, csrf, ct);
                }
            }
        }
        catch (Exception e)
        {
            message = $"无法连接 B站服务器（{e.Message}），只删除了本机的登录信息。";
        }
        finally
        {
            try { File.Delete(path); } catch (Exception e) { Log.Warn("删除 B站 Cookie 文件失败：" + e.Message); }
            Status = BiliStatus.NotLoggedIn;
            StatusChanged?.Invoke();
        }
        Log.Info("已退出 B站登录：" + message);
        return (revoked, message);
    }

    private static async Task<(bool, string)> RevokeOnServerAsync(CookieContainer jar, string csrf, CancellationToken ct)
    {
        using var http = CreateClient(jar);
        http.DefaultRequestHeaders.Add("Origin", "https://www.bilibili.com");
        using var form = new FormUrlEncodedContent(new Dictionary<string, string> { ["biliCSRF"] = csrf });
        using var resp = await http.PostAsync("https://passport.bilibili.com/login/exit/v2", form, ct);
        var text = await resp.Content.ReadAsStringAsync(ct);

        int code;
        string apiMsg = "";
        try
        {
            var j = JsonDocument.Parse(text).RootElement;
            code = j.GetProperty("code").GetInt32();
            if (j.TryGetProperty("message", out var m)) apiMsg = m.GetString() ?? "";
        }
        catch
        {
            return (false, $"B站服务器返回了无法识别的内容（HTTP {(int)resp.StatusCode}），只删除了本机的登录信息。");
        }

        return code switch
        {
            0 => (true, "已通知 B站服务器作废这次登录，本机的登录信息也已删除。"),
            -101 => (true, "这次登录在 B站服务器上本来就已经失效，本机的登录信息已删除。"),
            _ => (false, $"B站服务器没有接受退出请求（{code} {apiMsg}），只删除了本机的登录信息。" +
                         "可以在哔哩哔哩 App 的登录设备管理里手动下线。"),
        };
    }
}
