using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VDownloader.Models;
using VDownloader.Services;

namespace VDownloader.Views;

/// <summary>对话框的选择结果。Format 为 null 表示按预设画质下载。</summary>
public sealed record FormatChoice(string? Format, string? Label, bool ForceMkv);

public sealed partial class FormatDialog : ContentDialog
{
    private const string AutoAudio = "最佳音质（自动选择）";
    private readonly ProbeResult _info;
    private bool _goLogin;

    private FormatDialog(ProbeResult info)
    {
        InitializeComponent();
        _info = info;

        TitleText.Text = info.Title;
        MetaText.Text = string.Join("  ·  ", new[]
        {
            info.Site, info.Uploader, UrlTools.Duration(info.Duration),
            info.IsPlaylist ? $"合集 / 播放列表，共 {info.EntryCount} 个视频" : "",
        }.Where(s => !string.IsNullOrEmpty(s)));

        var preset = AppSettings.Current.Quality;
        if (info.IsPlaylist || info.Videos.Count == 0)
        {
            FormatList.Visibility = AudioRow.Visibility = Visibility.Collapsed;
            PrimaryButtonText = info.IsPlaylist ? $"全部下载（{preset}）" : "直接下载";
            SecondaryButtonText = "";
            NoteText.Text = info.IsPlaylist
                ? "合集会按首页选择的预设画质逐个下载，并放进以合集命名的文件夹里。"
                : "这个网站只提供单一格式，将直接下载。";
        }
        else
        {
            FormatList.ItemsSource = info.Videos;
            FormatList.SelectedIndex = 0;
            var audios = new List<object> { AutoAudio };
            audios.AddRange(info.Audios);
            AudioBox.ItemsSource = audios;
            AudioBox.SelectedIndex = 0;
            AudioRow.Visibility = info.Audios.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            SecondaryButtonText = $"按预设（{preset}）";
            NoteText.Text = "列表按清晰度从高到低排列；视频与音轨会自动合并成一个文件。";
        }

        if (info.IsBili)
        {
            var st = BiliService.Status;
            if (st.LoggedIn != true)
            {
                HintBar.Severity = InfoBarSeverity.Warning;
                HintBar.Title = "未登录 B站";
                HintBar.Message = "目前只能看到免费画质。扫码登录后可解锁 1080P 高码率；大会员还可下载 4K / HDR / 杜比视界 / 8K。";
                HintBar.IsOpen = true;
            }
            else if (!st.Vip && info.Videos.All(v => v.Height < 2160))
            {
                HintBar.Severity = InfoBarSeverity.Informational;
                HintBar.Title = $"已登录：{st.Name}";
                HintBar.Message = "4K、HDR、杜比视界、8K 等画质需要大会员账号。";
                HintAction.Visibility = Visibility.Collapsed;
                HintBar.IsOpen = true;
            }
        }
    }

    private void HintAction_Click(object sender, RoutedEventArgs e)
    {
        _goLogin = true;
        Hide();
    }

    private FormatChoice? BuildChoice(ContentDialogResult result)
    {
        if (result == ContentDialogResult.None) return null;
        bool presetMode = result == ContentDialogResult.Secondary || _info.IsPlaylist || _info.Videos.Count == 0;
        if (presetMode) return new FormatChoice(null, null, false);

        if (FormatList.SelectedItem is not VideoFormatRow v) return new FormatChoice(null, null, false);
        var container = AppSettings.Current.Container;
        var audio = AudioBox.SelectedItem as AudioFormatRow;
        string fmt = v.HasAudio && audio is null
            ? v.FormatId
            : audio is null
                ? $"{v.FormatId}+{FormatPresets.BestAudio(container)}/{v.FormatId}+ba/{v.FormatId}"
                : $"{v.FormatId}+{audio.FormatId}";
        var label = string.Join(" · ", new[] { v.Label, v.Codec, v.Hdr }.Where(s => !string.IsNullOrEmpty(s)));
        // MP4 装不下 FLAC 无损音轨，自动改用 MKV
        bool forceMkv = audio is { IsFlac: true } && container == "MP4";
        return new FormatChoice(fmt, label, forceMkv);
    }

    public static async Task<FormatChoice?> ShowAsync(ProbeResult info, XamlRoot root)
    {
        var dlg = new FormatDialog(info) { XamlRoot = root };
        var result = await dlg.ShowAsync();
        if (dlg._goLogin)
        {
            App.MainWindow.NavigateTo("account");
            return null;
        }
        return dlg.BuildChoice(result);
    }
}
