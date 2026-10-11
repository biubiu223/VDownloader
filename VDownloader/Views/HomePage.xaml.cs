using System.Collections.Specialized;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VDownloader.Models;
using VDownloader.Services;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;

namespace VDownloader.Views;

public sealed partial class HomePage : Page
{
    private bool _loading = true;

    public HomePage()
    {
        InitializeComponent();

        var s = AppSettings.Current;
        QualityBox.ItemsSource = FormatPresets.Qualities;
        CodecBox.ItemsSource = FormatPresets.CodecPrefs;
        ContainerBox.ItemsSource = FormatPresets.Containers;
        QualityBox.SelectedItem = s.Quality;
        CodecBox.SelectedItem = s.CodecPref;
        ContainerBox.SelectedItem = s.Container;
        PlaylistCheck.IsChecked = s.Playlist;
        SubsCheck.IsChecked = s.Subtitles;
        ThumbCheck.IsChecked = s.EmbedThumbnail;
        DirBox.Text = s.DownloadDir;
        _loading = false;

        var mgr = DownloadManager.Instance;
        RecentRepeater.ItemsSource = mgr.Recent;
        mgr.Recent.CollectionChanged += Recent_CollectionChanged;
        Recent_CollectionChanged(null, null);

        BiliService.StatusChanged += () => DispatcherQueue.TryEnqueue(UpdateBiliHint);
        UpdateBiliHint();

        Loaded += (_, _) => DirBox.Text = AppSettings.Current.DownloadDir;
    }

    private void Recent_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs? e)
    {
        bool any = DownloadManager.Instance.Recent.Count > 0;
        RecentEmpty.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
        RecentRepeater.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
    }

    private void HeroGrid_SizeChanged(object sender, SizeChangedEventArgs e) =>
        HeroMock.Visibility = e.NewSize.Width >= 760 ? Visibility.Visible : Visibility.Collapsed;

    private void UpdateBiliHint()
    {
        var st = BiliService.Status;
        if (st.LoggedIn == true)
        {
            BiliDot.Fill = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorSuccessBrush"];
            BiliHintText.Text = st.Vip
                ? $"B站已登录：{st.Name}（{st.VipLabel}）· 可下载 4K / HDR / 杜比视界 / 8K"
                : $"B站已登录：{st.Name} · 可下载 1080P / 1080P60（4K 等需大会员）";
            BiliLoginLink.Visibility = Visibility.Collapsed;
            BiliTileText.Text = st.Vip ? $"已登录 {st.Name} · {st.VipLabel}" : $"已登录 {st.Name}";
        }
        else
        {
            BiliDot.Fill = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCautionBrush"];
            BiliHintText.Text = st.LoggedIn is null
                ? "暂时无法检查 B站登录状态（网络问题）"
                : "B站未登录：部分视频最高只能下载 480P / 1080P";
            BiliLoginLink.Visibility = Visibility.Visible;
            BiliTileText.Text = "解锁 1080P+、4K、HDR、杜比视界与 8K";
        }
    }

    // ------------------------------------------------------------------ 选项
    private void Option_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        var s = AppSettings.Current;
        if (QualityBox.SelectedItem is string q) s.Quality = q;
        if (CodecBox.SelectedItem is string c) s.CodecPref = c;
        if (ContainerBox.SelectedItem is string f) s.Container = f;
        s.Playlist = PlaylistCheck.IsChecked == true;
        s.Subtitles = SubsCheck.IsChecked == true;
        s.EmbedThumbnail = ThumbCheck.IsChecked == true;
        bool audio = FormatPresets.IsAudioOnly(s.Quality);
        CodecBox.IsEnabled = ContainerBox.IsEnabled = !audio;
        AppSettings.Save();
    }

    private void UrlBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var n = UrlTools.ExtractUrls(UrlBox.Text).Count;
        UrlCountText.Text = n > 0 ? $"已识别 {n} 个链接" : "";
    }

    private async void Paste_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var content = Clipboard.GetContent();
            if (!content.Contains(StandardDataFormats.Text)) return;
            var text = await content.GetTextAsync();
            UrlBox.Text = string.IsNullOrWhiteSpace(UrlBox.Text) ? text : UrlBox.Text.TrimEnd() + Environment.NewLine + text;
        }
        catch { }
    }

    private void Clear_Click(object sender, RoutedEventArgs e) => UrlBox.Text = "";

    private async void ChangeDir_Click(object sender, RoutedEventArgs e)
    {
        var dir = await PickFolderAsync();
        if (dir is null) return;
        AppSettings.Current.DownloadDir = dir;
        AppSettings.Save();
        DirBox.Text = dir;
    }

    public static async Task<string?> PickFolderAsync()
    {
        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.Downloads };
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow));
        var folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }

    public static void OpenFolder(string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
            System.Diagnostics.Process.Start("explorer.exe", $"\"{dir}\"");
        }
        catch (Exception ex)
        {
            App.MainWindow.ShowMessage("打不开文件夹", ex.Message, InfoBarSeverity.Error);
        }
    }

    private void OpenDir_Click(object sender, RoutedEventArgs e) => OpenFolder(AppSettings.Current.DownloadDir);

    // ------------------------------------------------------------------ 下载
    private void Download_Click(object sender, RoutedEventArgs e)
    {
        var urls = UrlTools.ExtractUrls(UrlBox.Text);
        if (urls.Count == 0)
        {
            App.MainWindow.ShowMessage("没有识别到链接", "请在输入框里粘贴视频页面的网址，或 B站 BV 号 / 分享文字。", InfoBarSeverity.Warning);
            UrlBox.Focus(FocusState.Programmatic);
            return;
        }
        UrlBox.Text = "";
        App.MainWindow.QueueUrls(urls);
    }

    private async void Parse_Click(object sender, RoutedEventArgs e)
    {
        var urls = UrlTools.ExtractUrls(UrlBox.Text);
        if (urls.Count == 0)
        {
            App.MainWindow.ShowMessage("没有识别到链接", "请先粘贴一个视频链接再解析。", InfoBarSeverity.Warning);
            return;
        }
        var url = urls[0];
        SetParsing(true);
        try
        {
            using var doc = await YtDlp.ProbeAsync(url, AppSettings.Current.Playlist);
            var info = ProbeResult.Parse(url, doc);
            SetParsing(false);
            var choice = await FormatDialog.ShowAsync(info, XamlRoot);
            if (choice is null) return;

            var opts = DownloadOptions.FromSettings(choice.Format, choice.Label);
            if (choice.ForceMkv) opts = opts with { Container = "MKV" };
            App.MainWindow.QueueUrls([url], opts);
            // 链接框里只移除已经下载的这一个
            if (urls.Count == 1) UrlBox.Text = "";
        }
        catch (Exception ex)
        {
            SetParsing(false);
            await new ContentDialog
            {
                Title = "解析失败",
                Content = new TextBlock { Text = ex.Message, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true },
                CloseButtonText = "知道了",
                XamlRoot = XamlRoot,
            }.ShowAsync();
        }
        finally
        {
            SetParsing(false);
        }
    }

    private void SetParsing(bool busy)
    {
        ParseButton.IsEnabled = !busy;
        ParseRing.IsActive = busy;
        ParseRing.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        ParseIcon.Visibility = busy ? Visibility.Collapsed : Visibility.Visible;
        ParseText.Text = busy ? "正在解析…" : "解析并选择画质";
    }

    // ------------------------------------------------------------------ 导航卡片
    private async void Tile_Click(object sender, RoutedEventArgs e)
    {
        switch ((sender as FrameworkElement)?.Tag as string)
        {
            case "new":
                NewCard.StartBringIntoView(new BringIntoViewOptions { VerticalAlignmentRatio = 0.1, AnimationDesired = true });
                UrlBox.Focus(FocusState.Programmatic);
                break;
            case "folder":
                OpenFolder(AppSettings.Current.DownloadDir);
                break;
            case "sites":
                await Windows.System.Launcher.LaunchUriAsync(new Uri("https://github.com/yt-dlp/yt-dlp/blob/master/supportedsites.md"));
                break;
            case { } tag:
                App.MainWindow.NavigateTo(tag);
                break;
        }
    }

    private void BiliLogin_Click(object sender, RoutedEventArgs e) => App.MainWindow.NavigateTo("account");
    private void AllTasks_Click(object sender, RoutedEventArgs e) => App.MainWindow.NavigateTo("tasks");
    private void RecentItem_Click(object sender, RoutedEventArgs e) => App.MainWindow.NavigateTo("tasks");
}
