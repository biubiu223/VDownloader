using System.Collections.Specialized;
using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using VDownloader.Models;
using VDownloader.Services;
using Windows.ApplicationModel.DataTransfer;

namespace VDownloader.Views;

public sealed partial class TasksPage : Page
{
    private readonly DownloadManager _mgr = DownloadManager.Instance;

    public TasksPage()
    {
        InitializeComponent();
        TaskList.ItemsSource = _mgr.Tasks;
        _mgr.Tasks.CollectionChanged += OnChanged;
        _mgr.CountsChanged += () => DispatcherQueue.TryEnqueue(UpdateSummary);
        UpdateSummary();
    }

    private void OnChanged(object? sender, NotifyCollectionChangedEventArgs e) => UpdateSummary();

    private void UpdateSummary()
    {
        var t = _mgr.Tasks;
        EmptyState.Visibility = t.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        int active = t.Count(x => x.IsActive), done = t.Count(x => x.State == TaskState.Completed),
            failed = t.Count(x => x.State == TaskState.Failed);
        SummaryText.Text = t.Count == 0
            ? $"同时最多下载 {AppSettings.Current.MaxConcurrent} 个任务，可在设置中修改"
            : string.Join("  ·  ", new[]
            {
                active > 0 ? $"{active} 个进行中" : "", done > 0 ? $"{done} 个已完成" : "", failed > 0 ? $"{failed} 个失败" : "",
            }.Where(s => s.Length > 0));
    }

    private static DownloadTask? TaskOf(object sender) =>
        (sender as FrameworkElement)?.Tag as DownloadTask ?? (sender as FrameworkElement)?.DataContext as DownloadTask;

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        if (TaskOf(sender)?.FilePath is { } f && File.Exists(f))
            Process.Start(new ProcessStartInfo(f) { UseShellExecute = true });
    }

    private void Reveal_Click(object sender, RoutedEventArgs e)
    {
        var t = TaskOf(sender);
        if (t is null) return;
        if (t.FilePath is { } f && File.Exists(f))
            Process.Start("explorer.exe", $"/select,\"{f}\"");
        else
            HomePage.OpenFolder(t.Options.DownloadDir);
    }

    private void Retry_Click(object sender, RoutedEventArgs e)
    {
        if (TaskOf(sender) is { } t) _mgr.Retry(t);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (TaskOf(sender) is { } t) _mgr.Cancel(t);
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (TaskOf(sender) is { } t) _mgr.Remove(t);
    }

    private void CopyUrl_Click(object sender, RoutedEventArgs e)
    {
        if (TaskOf(sender) is not { } t) return;
        var dp = new DataPackage();
        dp.SetText(t.Url);
        Clipboard.SetContent(dp);
        App.MainWindow.ShowMessage("已复制", t.Url, InfoBarSeverity.Success, 3);
    }

    private async void OpenPage_Click(object sender, RoutedEventArgs e)
    {
        if (TaskOf(sender) is { } t) await Windows.System.Launcher.LaunchUriAsync(new Uri(t.Url));
    }

    private async void ShowLog_Click(object sender, RoutedEventArgs e)
    {
        if (TaskOf(sender) is not { } t) return;
        var log = t.GetLog();
        var box = new TextBox
        {
            Text = string.IsNullOrEmpty(log) ? "（还没有输出）" : log,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new FontFamily("Cascadia Mono, Consolas"),
            FontSize = 12,
            Height = 420,
            Width = 680,
        };
        ScrollViewer.SetVerticalScrollBarVisibility(box, ScrollBarVisibility.Auto);
        var dlg = new ContentDialog
        {
            Title = $"任务日志 — {t.Title}",
            Content = box,
            CloseButtonText = "关闭",
            PrimaryButtonText = "复制全部",
            XamlRoot = XamlRoot,
        };
        dlg.Resources["ContentDialogMaxWidth"] = 800.0;
        if (await dlg.ShowAsync() == ContentDialogResult.Primary)
        {
            var dp = new DataPackage();
            dp.SetText(box.Text);
            Clipboard.SetContent(dp);
        }
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e) => HomePage.OpenFolder(AppSettings.Current.DownloadDir);
    private void CancelAll_Click(object sender, RoutedEventArgs e) => _mgr.CancelAll();
    private void ClearFinished_Click(object sender, RoutedEventArgs e) => _mgr.ClearFinished();
    private void GoHome_Click(object sender, RoutedEventArgs e) => App.MainWindow.NavigateTo("home");
}
