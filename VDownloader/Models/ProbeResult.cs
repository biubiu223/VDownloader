using System.Text.Json;
using VDownloader.Services;

namespace VDownloader.Models;

/// <summary>“解析”得到的视频信息和可选画质列表。</summary>
public sealed class ProbeResult
{
    public string Url { get; init; } = "";
    public string Title { get; init; } = "";
    public string Uploader { get; init; } = "";
    public string Site { get; init; } = "";
    public double? Duration { get; init; }
    public bool IsPlaylist { get; init; }
    public int EntryCount { get; init; }
    public bool IsBili { get; init; }
    public List<VideoFormatRow> Videos { get; } = [];
    public List<AudioFormatRow> Audios { get; } = [];

    public static readonly Dictionary<int, string> BiliQn = new()
    {
        [6] = "240P 极速", [16] = "360P 流畅", [32] = "480P 清晰", [64] = "720P 高清", [74] = "720P60 高帧率",
        [80] = "1080P 高清", [100] = "智能修复", [112] = "1080P+ 高码率", [116] = "1080P60 高帧率",
        [120] = "4K 超清", [125] = "HDR 真彩", [126] = "杜比视界", [127] = "8K 超高清",
    };

    public static ProbeResult Parse(string url, JsonDocument doc)
    {
        var root = doc.RootElement;
        var extractor = Str(root, "extractor_key") ?? Str(root, "extractor") ?? "";
        var type = Str(root, "_type") ?? "video";
        var isList = type is "playlist" or "multi_video";
        var isBili = extractor.StartsWith("BiliBili", StringComparison.OrdinalIgnoreCase) || UrlTools.IsBili(url);

        var r = new ProbeResult
        {
            Url = url,
            Title = Str(root, "title") ?? Str(root, "id") ?? url,
            Uploader = Str(root, "uploader") ?? Str(root, "channel") ?? "",
            Site = UrlTools.SiteInfo(url, extractor).Name,
            Duration = Num(root, "duration"),
            IsPlaylist = isList,
            EntryCount = isList
                ? (int)(Num(root, "playlist_count")
                        ?? (root.TryGetProperty("entries", out var en) && en.ValueKind == JsonValueKind.Array ? en.GetArrayLength() : 0))
                : 0,
            IsBili = isBili,
        };
        if (isList || !root.TryGetProperty("formats", out var formats) || formats.ValueKind != JsonValueKind.Array)
            return r;

        var all = formats.EnumerateArray().ToList();
        bool hasHttpsVideo = all.Any(f => IsVideo(f) && !(Str(f, "protocol") ?? "").StartsWith("m3u8"));

        foreach (var f in all)
        {
            var id = Str(f, "format_id");
            if (id is null || Str(f, "ext") == "mhtml") continue;
            var size = Num(f, "filesize") ?? Num(f, "filesize_approx");

            if (IsVideo(f))
            {
                if (hasHttpsVideo && (Str(f, "protocol") ?? "").StartsWith("m3u8")) continue;
                var h = (int)(Num(f, "height") ?? 0);
                var w = (int)(Num(f, "width") ?? 0);
                var fps = Num(f, "fps");
                var q = Num(f, "quality");
                string label;
                if (isBili && q is not null && BiliQn.TryGetValue((int)q, out var qn)) label = qn;
                else label = Str(f, "format_note") is { Length: > 0 } note && note != "DASH video" ? note
                    : h > 0 ? $"{h}p" : id;
                var hdr = Str(f, "dynamic_range");
                r.Videos.Add(new VideoFormatRow
                {
                    FormatId = id,
                    Label = label,
                    Height = h,
                    Fps = fps ?? 0,
                    Tbr = Num(f, "tbr") ?? 0,
                    Resolution = w > 0 && h > 0 ? $"{w}×{h}" : (h > 0 ? $"{h}p" : "—"),
                    FpsText = fps is > 0 ? $"{fps:0.#} fps" : "",
                    Codec = VCodec(Str(f, "vcodec")),
                    Hdr = hdr is null or "SDR" ? "" : hdr == "DV" ? "杜比视界" : hdr,
                    SizeBytes = size,
                    HasAudio = (Str(f, "acodec") ?? "none") != "none",
                });
            }
            else if ((Str(f, "acodec") ?? "none") != "none")
            {
                var abr = Num(f, "abr") ?? Num(f, "tbr");
                r.Audios.Add(new AudioFormatRow
                {
                    FormatId = id,
                    Abr = abr ?? 0,
                    Label = $"{ACodec(Str(f, "acodec"))}  {(abr is > 0 ? $"{abr:0} kbps" : "")}  {UrlTools.Size(size)}".Trim(),
                    IsFlac = (Str(f, "acodec") ?? "").Contains("flac"),
                });
            }
        }

        r.Videos.Sort((a, b) => b.Height != a.Height ? b.Height.CompareTo(a.Height)
            : b.Fps != a.Fps ? b.Fps.CompareTo(a.Fps) : b.Tbr.CompareTo(a.Tbr));
        r.Audios.Sort((a, b) => b.Abr.CompareTo(a.Abr));
        return r;
    }

    private static bool IsVideo(JsonElement f) => (Str(f, "vcodec") ?? "none") != "none";

    public static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    public static double? Num(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;

    public static string VCodec(string? c)
    {
        c = (c ?? "").ToLowerInvariant();
        if (c.StartsWith("av01") || c == "av1") return "AV1";
        if (c.StartsWith("vp09") || c.StartsWith("vp9")) return "VP9";
        if (c.StartsWith("dvh1") || c.StartsWith("dvhe") || c.StartsWith("dvav")) return "杜比视界";
        if (c.StartsWith("hev1") || c.StartsWith("hvc1") || c.StartsWith("hevc") || c.StartsWith("h265")) return "HEVC";
        if (c.StartsWith("avc") || c.StartsWith("h264")) return "H.264";
        if (c.StartsWith("vp8")) return "VP8";
        return c.Length == 0 || c == "none" ? "" : c.Split('.')[0].ToUpperInvariant();
    }

    public static string ACodec(string? c)
    {
        c = (c ?? "").ToLowerInvariant();
        if (c.StartsWith("mp4a") || c == "aac") return "AAC";
        if (c.StartsWith("opus")) return "Opus";
        if (c.StartsWith("flac")) return "FLAC 无损";
        if (c.StartsWith("ec-3") || c.StartsWith("eac3")) return "杜比全景声";
        if (c.StartsWith("ac-3") || c.StartsWith("ac3")) return "AC-3";
        if (c.StartsWith("mp3")) return "MP3";
        if (c.StartsWith("vorbis")) return "Vorbis";
        return c.Length == 0 || c == "none" ? "" : c.Split('.')[0].ToUpperInvariant();
    }
}

public sealed class VideoFormatRow
{
    public string FormatId { get; init; } = "";
    public string Label { get; init; } = "";
    public string Resolution { get; init; } = "";
    public string FpsText { get; init; } = "";
    public string Codec { get; init; } = "";
    public string Hdr { get; init; } = "";
    public double? SizeBytes { get; init; }
    public bool HasAudio { get; init; }
    public int Height { get; init; }
    public double Fps { get; init; }
    public double Tbr { get; init; }

    public string SizeText => SizeBytes is > 0 ? "≈ " + UrlTools.Size(SizeBytes) : "";
    public string Detail => string.Join("  ·  ", new[] { Resolution, FpsText, Codec, Hdr, HasAudio ? "含音频" : "" }
        .Where(s => !string.IsNullOrEmpty(s)));
    public Microsoft.UI.Xaml.Visibility HdrVisibility =>
        string.IsNullOrEmpty(Hdr) ? Microsoft.UI.Xaml.Visibility.Collapsed : Microsoft.UI.Xaml.Visibility.Visible;
}

public sealed class AudioFormatRow
{
    public string FormatId { get; init; } = "";
    public string Label { get; init; } = "";
    public double Abr { get; init; }
    public bool IsFlac { get; init; }
    public override string ToString() => Label;
}
