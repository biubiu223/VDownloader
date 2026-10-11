using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using VDownloader.Services;
using Windows.Storage.Pickers;

namespace VDownloader.Views;

public sealed partial class SettingsPage : Page
{
    private bool _loading = true;
    private bool _busy;

    public SettingsPage()
    {
        InitializeComponent();
        var s = AppSettings.Current;
        SelectByTag(ThemeBox, s.Theme);
        ConcurrentBox.Value = s.MaxConcurrent;
        TemplateBox.Text = s.OutTemplate;
        NotifySwitch.IsOn = s.Notify;
        ClipSwitch.IsOn = s.WatchClipboard;
        UpdateSwitch.IsOn = s.CheckUpdates;
        SelectByTag(ProxyModeBox, s.ProxyMode);
        ProxyBox.Text = s.Proxy;
        CnDirectSwitch.IsOn = s.CnDirect;
        SelectByTag(CookieModeBox, s.CookieMode);
        SelectByTag(BrowserBox, s.CookieBrowser);
        AboutCard.Description = $"版本 {AppInfo.Version} · 原生 WinUI 3 界面 · 基于 yt-dlp · {AppInfo.RepoUrl}";
        _loading = false;
        UpdateDependentControls();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        DirCard.Description = AppSettings.Current.DownloadDir;
        _ = RefreshComponentsAsync();
    }

    private static void SelectByTag(ComboBox box, string tag)
    {
        box.SelectedItem = box.Items.OfType<ComboBoxItem>().FirstOrDefault(i => i.Tag as string == tag) ?? box.Items[0];
    }

    private static string TagOf(ComboBox box) => (box.SelectedItem as ComboBoxItem)?.Tag as string ?? "";

    private void UpdateDependentControls()
    {
        var s = AppSettings.Current;
        ProxyBox.IsEnabled = s.ProxyMode == "custom";
        BrowserCard.Visibility = s.CookieMode == "browser" ? Visibility.Visible : Visibility.Collapsed;
        CookieFileCard.Visibility = s.CookieMode == "file" ? Visibility.Visible : Visibility.Collapsed;
        CookieFileCard.Description = string.IsNullOrEmpty(s.CookieFile) ? "还没有选择文件" : s.CookieFile;
    }

    // ------------------------------------------------------------------ 外观 / 下载
    private void ThemeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        AppSettings.Current.Theme = TagOf(ThemeBox);
        AppSettings.Save();
        App.MainWindow.ApplyTheme(AppSettings.Current.Theme);
    }

    private void OpenDir_Click(object sender, RoutedEventArgs e) => HomePage.OpenFolder(AppSettings.Current.DownloadDir);

    private async void ChangeDir_Click(object sender, RoutedEventArgs e)
    {
        var dir = await HomePage.PickFolderAsync();
        if (dir is null) return;
        AppSettings.Current.DownloadDir = dir;
        AppSettings.Save();
        DirCard.Description = dir;
    }

    private void ConcurrentBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_loading || double.IsNaN(args.NewValue)) return;
        AppSettings.Current.MaxConcurrent = (int)Math.Clamp(args.NewValue, 1, 5);
        AppSettings.Save();
        DownloadManager.Instance.Schedule();
    }

    private void TemplateBox_LostFocus(object sender, RoutedEventArgs e)
    {
        var t = TemplateBox.Text.Trim();
        if (string.IsNullOrEmpty(t) || !t.Contains("%(")) t = AppSettings.DefaultOutTemplate;
        TemplateBox.Text = t;
        AppSettings.Current.OutTemplate = t;
        AppSettings.Save();
    }

    private void ResetTemplate_Click(object sender, RoutedEventArgs e)
    {
        TemplateBox.Text = AppSettings.DefaultOutTemplate;
        AppSettings.Current.OutTemplate = AppSettings.DefaultOutTemplate;
        AppSettings.Save();
    }

    private void Switch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        var s = AppSettings.Current;
        s.Notify = NotifySwitch.IsOn;
        s.WatchClipboard = ClipSwitch.IsOn;
        s.CnDirect = CnDirectSwitch.IsOn;
        s.CheckUpdates = UpdateSwitch.IsOn;
        AppSettings.Save();
    }

    private async void CheckUpdate_Click(object sender, RoutedEventArgs e) =>
        await App.MainWindow.CheckForUpdateAsync(silent: false);

    private async void OpenRepo_Click(object sender, RoutedEventArgs e) =>
        await Windows.System.Launcher.LaunchUriAsync(new Uri(AppInfo.RepoUrl));

    // ------------------------------------------------------------------ 网络 / Cookie
    private void ProxyModeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        AppSettings.Current.ProxyMode = TagOf(ProxyModeBox);
        AppSettings.Save();
        UpdateDependentControls();
    }

    private void ProxyBox_LostFocus(object sender, RoutedEventArgs e)
    {
        var p = ProxyBox.Text.Trim();
        if (p.Length > 0 && !p.Contains("://")) p = "http://" + p;
        ProxyBox.Text = p;
        AppSettings.Current.Proxy = p;
        AppSettings.Save();
    }

    private void CookieModeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        AppSettings.Current.CookieMode = TagOf(CookieModeBox);
        AppSettings.Save();
        UpdateDependentControls();
    }

    private void BrowserBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        AppSettings.Current.CookieBrowser = TagOf(BrowserBox);
        AppSettings.Save();
    }

    private async void PickCookie_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".txt");
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow));
        var file = await picker.PickSingleFileAsync();
        if (file is null) return;
        AppSettings.Current.CookieFile = file.Path;
        AppSettings.Save();
        UpdateDependentControls();
    }

    // ------------------------------------------------------------------ 组件
    private async Task RefreshComponentsAsync()
    {
        Tools.Refresh();
        YtDlpCard.Description = FfmpegCard.Description = DenoCard.Description = "正在检测…";
        var vY = Tools.GetVersionAsync(Tools.YtDlp);
        var vF = Tools.GetVersionAsync(Tools.Ffmpeg, "-version");
        var vD = Tools.GetVersionAsync(Tools.Deno);
        await Task.WhenAll(vY, vF, vD);

        YtDlpCard.Description = Describe(Tools.YtDlp, vY.Result, "没有找到，点右侧按钮自动下载");
        FfmpegCard.Description = Describe(Tools.Ffmpeg, vF.Result, "没有找到：只能下载画质较低的音视频一体格式");
        DenoCard.Description = Describe(Tools.Deno, vD.Result, "没有找到：YouTube 可能缺少高画质格式");
        YtDlpButton.Content = Tools.YtDlp.Found ? "检查更新" : "下载安装";
        FfmpegButton.Content = Tools.Ffmpeg.Found ? "重新下载" : "下载安装";
        DenoButton.Content = Tools.Deno.Found ? "重新下载" : "下载安装";
        FfmpegButton.Visibility = Tools.Ffmpeg.Source is "Python 环境" or "系统 PATH" ? Visibility.Collapsed : Visibility.Visible;
        DenoButton.Visibility = Tools.Deno.Source is "Python 环境" or "系统 PATH" ? Visibility.Collapsed : Visibility.Visible;
    }

    private static string Describe(ToolInfo t, string? version, string missing) =>
        !t.Found ? missing : $"版本 {version ?? "未知"} · 来源：{t.Source} · {t.Path}";

    private void SetBusy(bool busy, string text = "")
    {
        _busy = busy;
        ComponentPanel.Visibility = busy || text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        ComponentStatus.Text = text;
        ComponentProgress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        ComponentProgress.IsIndeterminate = true;
        YtDlpButton.IsEnabled = FfmpegButton.IsEnabled = DenoButton.IsEnabled = !busy;
    }

    private async void UpdateYtDlp_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        SetBusy(true, "正在更新 yt-dlp …");
        try
        {
            var ok = await Tools.UpdateYtDlpAsync(line =>
            {
                Log.Info("[更新] " + line);
                DispatcherQueue.TryEnqueue(() => ComponentStatus.Text = line);
            });
            SetBusy(false, ok ? "yt-dlp 已是最新版本。" : "更新失败，请查看「运行日志」。如果无法访问 GitHub，请先在上方设置代理。");
        }
        catch (Exception ex)
        {
            SetBusy(false, "更新失败：" + ex.Message);
        }
        await RefreshComponentsAsync();
    }

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || (sender as FrameworkElement)?.Tag is not string tool) return;
        var confirm = new ContentDialog
        {
            Title = "下载组件",
            Content = $"将从官方 GitHub 发布页下载 {Tools.Describe(tool)}，保存到：\n{AppPaths.UserToolsDir}\n\n下载 GitHub 文件可能需要代理。是否继续？",
            PrimaryButtonText = "下载",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };
        if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;

        SetBusy(true, $"正在下载 {Tools.Describe(tool)} …");
        try
        {
            var progress = new Progress<double>(p =>
            {
                ComponentProgress.IsIndeterminate = false;
                ComponentProgress.Value = p;
            });
            await Tools.InstallAsync(tool, line =>
            {
                Log.Info("[组件] " + line);
                DispatcherQueue.TryEnqueue(() => ComponentStatus.Text = line);
            }, progress);
            SetBusy(false, $"{Tools.Describe(tool)} 安装完成。");
        }
        catch (Exception ex)
        {
            SetBusy(false, "下载失败：" + ex.Message + "（如果无法访问 GitHub，请先在上方设置代理）");
        }
        await RefreshComponentsAsync();
    }

    private void OpenData_Click(object sender, RoutedEventArgs e) => HomePage.OpenFolder(AppPaths.DataDir);

    private async void OpenYtDlpHome_Click(object sender, RoutedEventArgs e) =>
        await Windows.System.Launcher.LaunchUriAsync(new Uri("https://github.com/yt-dlp/yt-dlp"));
}
