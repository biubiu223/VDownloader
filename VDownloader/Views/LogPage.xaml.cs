using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using VDownloader.Services;
using Windows.ApplicationModel.DataTransfer;

namespace VDownloader.Views;

public sealed partial class LogPage : Page
{
    private readonly DispatcherQueueTimer _timer;
    private volatile bool _dirty = true;

    public LogPage()
    {
        InitializeComponent();
        Log.Changed += () => _dirty = true;
        // 日志可能很频繁，每 0.5 秒最多刷新一次
        _timer = DispatcherQueue.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(500);
        _timer.Tick += (_, _) => Refresh();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _dirty = true;
        Refresh();
        _timer.Start();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _timer.Stop();
    }

    private void Refresh()
    {
        if (!_dirty) return;
        _dirty = false;
        bool atBottom = LogScroller.VerticalOffset >= LogScroller.ScrollableHeight - 40;
        var text = Log.Snapshot();
        LogText.Text = string.IsNullOrEmpty(text) ? "（暂无日志）" : text;
        if (atBottom)
        {
            LogScroller.UpdateLayout();
            LogScroller.ChangeView(null, LogScroller.ScrollableHeight, null, true);
        }
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        var dp = new DataPackage();
        dp.SetText(Log.Snapshot());
        Clipboard.SetContent(dp);
        App.MainWindow.ShowMessage("已复制", "运行日志已复制到剪贴板。", InfoBarSeverity.Success, 3);
    }

    private void Clear_Click(object sender, RoutedEventArgs e) => Log.Clear();

    private void OpenData_Click(object sender, RoutedEventArgs e) => HomePage.OpenFolder(AppPaths.DataDir);
}
