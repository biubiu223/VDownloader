using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using VDownloader.Models;
using VDownloader.Services;
using VDownloader.Views;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;

namespace VDownloader;

public sealed partial class MainWindow : Window
{
    private static readonly Dictionary<string, Type> PageMap = new()
    {
        ["home"] = typeof(HomePage),
        ["tasks"] = typeof(TasksPage),
        ["account"] = typeof(AccountPage),
        ["log"] = typeof(LogPage),
        ["settings"] = typeof(SettingsPage),
    };

    private readonly HashSet<string> _seenUrls = [];
    private List<string> _pendingUrls = [];
    private string? _lastClipboard;
    private bool _isActive = true;
    private bool _closeConfirmed;
    private DispatcherQueueTimer? _messageTimer;

    public MainWindow()
    {
        InitializeComponent();

        // Mica 背景 + 内容延伸到标题栏（和 WinUI 3 Gallery 一样）
        SystemBackdrop = new MicaBackdrop();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        if (File.Exists(AppPaths.IconFile)) AppWindow.SetIcon(AppPaths.IconFile);
        SetupWindowSize();
        ApplyTheme(AppSettings.Current.Theme);

        var mgr = DownloadManager.Instance;
        mgr.Initialize(DispatcherQueue);
        mgr.CountsChanged += UpdateBadge;
        mgr.TaskFinished += OnTaskFinished;

        Notifier.Init();
        Notifier.Invoked += () => DispatcherQueue.TryEnqueue(BringToFront);

        Activated += OnActivated;
        AppWindow.Closing += OnClosing;

        // 提示条收起时不占位置，展开时才留出上边距
        foreach (var bar in new[] { MessageBar, UpdateBar, ClipboardBar })
            bar.RegisterPropertyChangedCallback(InfoBar.IsOpenProperty, (d, _) =>
                ((InfoBar)d).Margin = ((InfoBar)d).IsOpen ? new Thickness(0, 12, 0, 0) : new Thickness(0));

        NavView.SelectedItem = NavView.MenuItems[0];
        _ = BiliService.RefreshStatusAsync();

        // 拷到新电脑上首次运行时，提示安装缺少的组件
        var missing = new[] { ("yt-dlp", Tools.YtDlp), ("FFmpeg", Tools.Ffmpeg), ("Deno", Tools.Deno) }
            .Where(x => !x.Item2.Found).Select(x => x.Item1).ToList();
        if (missing.Count > 0)
            DispatcherQueue.TryEnqueue(() => ShowMessage("缺少组件：" + string.Join("、", missing),
                "请打开「设置 → 组件」点击「下载安装」，一键从官方 GitHub 获取。", InfoBarSeverity.Warning, 30));

        if (AppSettings.Current.CheckUpdates) _ = CheckForUpdateAsync(silent: true);
    }

    // ------------------------------------------------------------------ 检查新版本
    private string _updateUrl = AppInfo.ReleasesUrl;

    /// <summary>检查 GitHub 上是否有新版本。silent = 启动时自动检查，没有新版本或网络失败都不打扰用户。</summary>
    public async Task CheckForUpdateAsync(bool silent)
    {
        try
        {
            if (silent) await Task.Delay(TimeSpan.FromSeconds(5));
            var rel = await UpdateChecker.CheckAsync();
            if (rel is null)
            {
                if (!silent) ShowMessage("已是最新版本", $"当前版本 v{AppInfo.Version}。", InfoBarSeverity.Success);
                return;
            }
            _updateUrl = rel.Url;
            UpdateBar.Message = $"v{rel.Version}（当前 v{AppInfo.Version}）已在 GitHub 发布。";
            UpdateBar.IsOpen = true;
            Log.Info($"发现新版本 {rel.Tag}：{rel.Url}");
        }
        catch (Exception e)
        {
            if (!silent) ShowMessage("检查更新失败", "无法连接 GitHub：" + e.Message, InfoBarSeverity.Warning);
        }
    }

    private async void UpdateDownload_Click(object sender, RoutedEventArgs e)
    {
        UpdateBar.IsOpen = false;
        await Windows.System.Launcher.LaunchUriAsync(new Uri(_updateUrl));
    }

    // ------------------------------------------------------------------ 窗口
    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

    private IntPtr Hwnd => WinRT.Interop.WindowNative.GetWindowHandle(this);

    private void SetupWindowSize()
    {
        var s = AppSettings.Current;
        double scale = GetDpiForWindow(Hwnd) / 96.0;
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        int w = s.WindowWidth > 600 ? s.WindowWidth : (int)(1180 * scale);
        int h = s.WindowHeight > 400 ? s.WindowHeight : (int)(800 * scale);
        w = Math.Min(w, area.Width);
        h = Math.Min(h, area.Height);
        AppWindow.MoveAndResize(new RectInt32(area.X + (area.Width - w) / 2, area.Y + (area.Height - h) / 2, w, h));
    }

    public void ApplyTheme(string theme)
    {
        if (Content is not FrameworkElement root) return;
        root.RequestedTheme = theme switch
        {
            "light" => ElementTheme.Light,
            "dark" => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };
        // 标题栏右上角三个按钮的颜色跟着主题走
        var dark = root.ActualTheme == ElementTheme.Dark;
        var tb = AppWindow.TitleBar;
        tb.ButtonBackgroundColor = Colors.Transparent;
        tb.ButtonInactiveBackgroundColor = Colors.Transparent;
        tb.ButtonForegroundColor = dark ? Colors.White : Colors.Black;
        tb.ButtonHoverForegroundColor = dark ? Colors.White : Colors.Black;
        tb.ButtonHoverBackgroundColor = dark ? Windows.UI.Color.FromArgb(0x18, 255, 255, 255) : Windows.UI.Color.FromArgb(0x10, 0, 0, 0);
        tb.ButtonPressedBackgroundColor = dark ? Windows.UI.Color.FromArgb(0x10, 255, 255, 255) : Windows.UI.Color.FromArgb(0x0A, 0, 0, 0);
        tb.ButtonInactiveForegroundColor = dark ? Windows.UI.Color.FromArgb(0x80, 255, 255, 255) : Windows.UI.Color.FromArgb(0x80, 0, 0, 0);
    }

    private void BringToFront()
    {
        if (AppWindow.Presenter is OverlappedPresenter p && p.State == OverlappedPresenterState.Minimized) p.Restore();
        Activate();
        SetForegroundWindow(Hwnd);
    }

    private async void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        var s = AppSettings.Current;
        if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Restored })
        {
            s.WindowWidth = AppWindow.Size.Width;
            s.WindowHeight = AppWindow.Size.Height;
        }
        AppSettings.SaveNow();

        var running = DownloadManager.Instance.Tasks.Count(t => t.IsActive);
        if (running > 0 && !_closeConfirmed)
        {
            args.Cancel = true;
            var dlg = new ContentDialog
            {
                Title = "还有任务没下载完",
                Content = $"有 {running} 个任务正在下载或排队。退出会中断下载（下次重新开始会自动续传）。确定退出吗？",
                PrimaryButtonText = "退出",
                CloseButtonText = "继续下载",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = Content.XamlRoot,
            };
            if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;
            _closeConfirmed = true;
            DownloadManager.Instance.CancelAll();
            Close();
            return;
        }
        Notifier.Shutdown();
    }

    // ------------------------------------------------------------------ 导航
    public void NavigateTo(string tag)
    {
        if (!PageMap.TryGetValue(tag, out var type)) return;
        if (ContentFrame.CurrentSourcePageType != type)
            ContentFrame.Navigate(type, null, new EntranceNavigationTransitionInfo());
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var tag = args.IsSettingsSelected ? "settings" : (args.SelectedItem as NavigationViewItem)?.Tag as string;
        if (tag is not null) NavigateTo(tag);
    }

    private async void NavView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if ((args.InvokedItemContainer as NavigationViewItem)?.Tag as string == "sites")
            await Windows.System.Launcher.LaunchUriAsync(new Uri("https://github.com/yt-dlp/yt-dlp/blob/master/supportedsites.md"));
    }

    private void ContentFrame_Navigated(object sender, NavigationEventArgs e)
    {
        BackButton.IsEnabled = ContentFrame.CanGoBack;
        var tag = PageMap.FirstOrDefault(kv => kv.Value == e.SourcePageType).Key;
        if (tag == "settings")
        {
            NavView.SelectedItem = NavView.SettingsItem;
        }
        else
        {
            var item = NavView.MenuItems.OfType<NavigationViewItem>().FirstOrDefault(i => i.Tag as string == tag);
            if (item is not null && !ReferenceEquals(NavView.SelectedItem, item)) NavView.SelectedItem = item;
        }
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (ContentFrame.CanGoBack) ContentFrame.GoBack();
    }

    // ------------------------------------------------------------------ 下载入口
    /// <summary>把文本中识别到的链接全部加入下载队列，返回数量。</summary>
    public int QueueUrls(IEnumerable<string> urls, DownloadOptions? options = null, bool goToTasks = true)
    {
        int n = 0;
        foreach (var u in urls)
        {
            _seenUrls.Add(u);
            DownloadManager.Instance.Add(u, options ?? DownloadOptions.FromSettings());
            n++;
        }
        if (n > 0 && goToTasks) NavigateTo("tasks");
        return n;
    }

    private void QuickBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        var urls = UrlTools.ExtractUrls(args.QueryText);
        if (urls.Count == 0)
        {
            ShowMessage("没有识别到链接", "请粘贴视频页面的网址，或 B站的 BV 号 / 分享文字。", InfoBarSeverity.Warning);
            return;
        }
        sender.Text = "";
        QueueUrls(urls);
    }

    public void ShowMessage(string title, string message, InfoBarSeverity severity = InfoBarSeverity.Informational, int seconds = 6)
    {
        MessageBar.Title = title;
        MessageBar.Message = message;
        MessageBar.Severity = severity;
        MessageBar.IsOpen = true;
        _messageTimer?.Stop();
        _messageTimer = DispatcherQueue.CreateTimer();
        _messageTimer.Interval = TimeSpan.FromSeconds(seconds);
        _messageTimer.IsRepeating = false;
        _messageTimer.Tick += (_, _) => MessageBar.IsOpen = false;
        _messageTimer.Start();
    }

    // ------------------------------------------------------------------ 剪贴板识别
    private async void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        _isActive = args.WindowActivationState != WindowActivationState.Deactivated;
        if (!_isActive || !AppSettings.Current.WatchClipboard) return;
        try
        {
            var content = Clipboard.GetContent();
            if (!content.Contains(StandardDataFormats.Text)) return;
            var text = await content.GetTextAsync();
            if (text == _lastClipboard) return;
            _lastClipboard = text;
            var known = DownloadManager.Instance.Tasks.Select(t => t.Url).ToHashSet();
            var urls = UrlTools.ExtractUrls(text).Where(u => !_seenUrls.Contains(u) && !known.Contains(u)).ToList();
            if (urls.Count == 0) return;
            _pendingUrls = urls;
            ClipboardBar.Message = urls.Count == 1 ? urls[0] : $"{urls[0]}  等 {urls.Count} 个链接";
            ClipboardBar.IsOpen = true;
        }
        catch
        {
            // 剪贴板被其它程序占用时忽略
        }
    }

    private void ClipboardDownload_Click(object sender, RoutedEventArgs e)
    {
        ClipboardBar.IsOpen = false;
        QueueUrls(_pendingUrls);
        _pendingUrls = [];
    }

    private void ClipboardBar_Closed(InfoBar sender, InfoBarClosedEventArgs args)
    {
        foreach (var u in _pendingUrls) _seenUrls.Add(u);
    }

    // ------------------------------------------------------------------ 状态
    private void UpdateBadge()
    {
        var active = DownloadManager.Instance.ActiveCount;
        TasksBadge.Value = active;
        TasksBadge.Visibility = active > 0 ? Visibility.Visible : Visibility.Collapsed;
        TitleBadge.Text = active > 0 ? $"·  {active} 个任务进行中" : "";
    }

    private void OnTaskFinished(DownloadTask t)
    {
        UpdateBadge();
        if (_isActive) return;
        if (t.State == TaskState.Completed) Notifier.Show("下载完成", t.Title);
        else if (t.State == TaskState.Failed) Notifier.Show("下载失败", $"{t.Title}\n{t.Error}");
    }
}
