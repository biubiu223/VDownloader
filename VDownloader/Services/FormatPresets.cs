namespace VDownloader.Services;

/// <summary>画质 / 编码 / 容器 预设，以及它们对应的 yt-dlp 参数。</summary>
public static class FormatPresets
{
    public static readonly string[] Qualities =
    [
        "最高画质", "4K (2160p)", "2K (1440p)", "1080p", "720p", "480p", "仅音频 (M4A)", "仅音频 (MP3)",
    ];

    public static readonly string[] CodecPrefs = ["画质优先", "兼容优先 (H.264/SDR)"];

    public static readonly string[] Containers = ["MP4", "MKV"];

    private static readonly Dictionary<string, int> Heights = new()
    {
        ["4K (2160p)"] = 2160, ["2K (1440p)"] = 1440, ["1080p"] = 1080, ["720p"] = 720, ["480p"] = 480,
    };

    public static bool IsAudioOnly(string quality) => quality.StartsWith("仅音频");

    /// <summary>MP4 装不下 FLAC 无损音轨（B站大会员 Hi-Res），选 MP4 时优先其它音轨。</summary>
    public static string BestAudio(string container) => container == "MP4" ? "ba[acodec!=flac]" : "ba";

    /// <summary>根据下载选项生成 yt-dlp 的格式相关参数。</summary>
    public static List<string> BuildArgs(DownloadOptions o)
    {
        var args = new List<string>();
        if (IsAudioOnly(o.Quality))
        {
            if (o.Quality.Contains("MP3"))
                args.AddRange(["-f", "ba/b", "-x", "--audio-format", "mp3", "--audio-quality", "0"]);
            else
                args.AddRange(["-f", "ba[ext=m4a]/ba/b", "-x", "--audio-format", "m4a"]);
            return args;
        }

        string spec;
        if (!string.IsNullOrEmpty(o.CustomFormat))
        {
            spec = o.CustomFormat;
        }
        else
        {
            var a = BestAudio(o.Container);
            if (Heights.TryGetValue(o.Quality, out var h))
            {
                var f = $"[height<={h}]";
                spec = $"bv*{f}+{a}/bv*{f}+ba/b{f}/bv*+ba/b";
            }
            else
            {
                spec = $"bv*+{a}/bv*+ba/b";
            }
        }
        args.AddRange(["-f", spec]);

        // 兼容优先：分辨率仍取最高，同分辨率下优先 H.264 + AAC + SDR，老电视/剪辑软件都能直接打开
        if (o.CodecPref != CodecPrefs[0])
            args.AddRange(["-S", "res,fps,hdr:sdr,vcodec:h264,acodec:aac"]);

        args.AddRange(["--merge-output-format", o.Container == "MKV" ? "mkv" : "mp4"]);
        return args;
    }
}

/// <summary>一个下载任务创建时的选项快照。</summary>
public sealed record DownloadOptions(
    string Quality,
    string CodecPref,
    string Container,
    bool Playlist,
    bool Subtitles,
    bool EmbedThumbnail,
    string DownloadDir,
    string? CustomFormat = null,
    string? CustomLabel = null)
{
    public static DownloadOptions FromSettings(string? customFormat = null, string? customLabel = null)
    {
        var s = AppSettings.Current;
        return new(s.Quality, s.CodecPref, s.Container, s.Playlist, s.Subtitles, s.EmbedThumbnail,
            s.DownloadDir, customFormat, customLabel);
    }
}
