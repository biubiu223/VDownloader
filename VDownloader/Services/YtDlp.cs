using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace VDownloader.Services;

/// <summary>拼装 yt-dlp 命令行参数，以及“解析”（只取信息不下载）。</summary>
public static class YtDlp
{
    /// <summary>
    /// 通用参数：编码、组件路径、代理、Cookie、合集开关。
    /// 返回需要在结束后删除的临时 Cookie 文件路径（yt-dlp 退出时会回写 Cookie，用副本避免并发写坏原文件）。
    /// </summary>
    public static List<string> CommonArgs(string url, bool playlist, out string? tempCookie)
    {
        var s = AppSettings.Current;
        var a = new List<string>
        {
            "--ignore-config", "--encoding", "utf-8", "--no-colors", "--newline",
            "--socket-timeout", "20", "--retries", "10", "--fragment-retries", "10",
        };

        if (Tools.Deno.Found) a.AddRange(["--js-runtimes", "deno:" + Tools.Deno.Path]);
        if (Tools.Ffmpeg.Found) a.AddRange(["--ffmpeg-location", Tools.Ffmpeg.Path!]);

        var proxy = ProxyFor(url);
        if (proxy is not null) a.AddRange(["--proxy", proxy]);

        // B站登录信息平时是加密的；yt-dlp 只能读明文文件，所以只在本次运行期间解密出一份临时副本，
        // 进程结束后立刻用 SecureStore.SecureDelete 覆盖删除。
        tempCookie = null;
        if (UrlTools.IsBili(url) && BiliService.GetCookieTextForDownload() is { } biliCookies)
        {
            tempCookie = SecureStore.WriteTempPlaintext(biliCookies);
        }
        else if (s.CookieMode == "file" && File.Exists(s.CookieFile))
        {
            tempCookie = SecureStore.WriteTempPlaintext(File.ReadAllText(s.CookieFile));
        }
        else if (s.CookieMode == "browser" && !string.IsNullOrWhiteSpace(s.CookieBrowser))
        {
            a.AddRange(["--cookies-from-browser", s.CookieBrowser]);
        }
        if (tempCookie is not null) a.AddRange(["--cookies", tempCookie]);

        a.Add(playlist ? "--yes-playlist" : "--no-playlist");
        return a;
    }

    /// <summary>null = 跟随系统代理；"" = 强制直连；其它 = 指定代理。</summary>
    public static string? ProxyFor(string url)
    {
        var s = AppSettings.Current;
        if (s.ProxyMode == "none" || (s.CnDirect && UrlTools.IsCnSite(url))) return "";
        if (s.ProxyMode == "custom" && !string.IsNullOrWhiteSpace(s.Proxy)) return s.Proxy.Trim();
        return null;
    }

    /// <summary>删除临时 Cookie 副本（先覆盖再删除）。</summary>
    public static void DeleteQuietly(string? path) => SecureStore.SecureDelete(path);

    /// <summary>解析链接：返回 yt-dlp -J 的 JSON（合集只列出条目，不逐个解析）。</summary>
    public static async Task<JsonDocument> ProbeAsync(string url, bool playlist, CancellationToken ct = default)
    {
        if (!Tools.YtDlp.Found) throw new InvalidOperationException("没有找到 yt-dlp 解析内核，请到「设置 → 组件」中安装。");
        var args = CommonArgs(url, playlist, out var tempCookie);
        args.AddRange(["-J", "--flat-playlist", "--no-warnings", "--", url]);

        var psi = Tools.CreateStartInfo(Tools.YtDlp.Path!, args);
        using var p = new Process { StartInfo = psi };
        var err = new StringBuilder();
        try
        {
            p.Start();
            using var reg = ct.Register(() => { try { p.Kill(true); } catch { } });
            var stdoutTask = p.StandardOutput.ReadToEndAsync();
            var stderrTask = Task.Run(async () =>
            {
                string? line;
                while ((line = await p.StandardError.ReadLineAsync()) is not null)
                {
                    err.AppendLine(line);
                    if (line.StartsWith("ERROR") || line.StartsWith("WARNING")) Log.Warn("[解析] " + line);
                }
            });
            await p.WaitForExitAsync(CancellationToken.None);
            var json = await stdoutTask;
            await stderrTask;
            ct.ThrowIfCancellationRequested();
            if (p.ExitCode != 0 || string.IsNullOrWhiteSpace(json))
                throw new InvalidOperationException(ErrorHints.Explain(err.ToString()));
            return JsonDocument.Parse(json);
        }
        finally
        {
            DeleteQuietly(tempCookie);
        }
    }
}

/// <summary>把 yt-dlp 的英文报错翻译成看得懂的中文提示。</summary>
public static class ErrorHints
{
    public static string Explain(string raw)
    {
        var lines = raw.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var error = lines.LastOrDefault(l => l.StartsWith("ERROR")) ?? lines.LastOrDefault() ?? "未知错误";
        var e = error.ToLowerInvariant();

        string? hint = null;
        if (e.Contains("not a bot") || e.Contains("sign in to confirm"))
            hint = "YouTube 要求验证身份：请在「设置 → 其他网站 Cookie」里选择从 Firefox 读取或导入 cookies.txt。";
        else if (e.Contains("unsupported url"))
            hint = "暂不支持这个链接，请确认是视频播放页地址。";
        else if (e.Contains("http error 412") || e.Contains("precondition failed"))
            hint = "被 B站风控拦截了，请先在「B站账号」页扫码登录，或稍等几分钟再试。";
        else if (e.Contains("http error 403"))
            hint = "服务器拒绝访问（403），可能需要登录 Cookie 或更换代理，也可以尝试更新 yt-dlp。";
        else if (e.Contains("requested format is not available"))
            hint = "所选画质不可用，请改用「最高画质」或重新解析后选择。";
        else if (e.Contains("private video") || e.Contains("video unavailable") || e.Contains("has been removed"))
            hint = "视频不可用：可能已删除、设为私密，或有地区限制。";
        else if (e.Contains("members-only") || e.Contains("premium") || e.Contains("大会员") || e.Contains("付费"))
            hint = "该视频需要会员/付费权限，请登录有权限的账号。";
        else if (e.Contains("age") && e.Contains("confirm"))
            hint = "视频有年龄限制，需要提供已登录账号的 Cookie。";
        else if (e.Contains("timed out") || e.Contains("unable to download") || e.Contains("connection")
                 || e.Contains("getaddrinfo") || e.Contains("proxy") || e.Contains("ssl"))
            hint = "网络连接失败：海外网站请确认代理已开启（设置 → 网络）。";
        else if (e.Contains("ffmpeg") || e.Contains("ffprobe"))
            hint = "缺少 FFmpeg，无法合并音视频，请到「设置 → 组件」安装。";
        else if (e.Contains("cookies") && e.Contains("database"))
            hint = "读取浏览器 Cookie 失败：Chrome/Edge 新版加密了 Cookie，建议改用 Firefox 或导出 cookies.txt。";

        var msg = error.Replace("ERROR: ", "");
        return hint is null ? msg : $"{hint}\n原始信息：{msg}";
    }
}
