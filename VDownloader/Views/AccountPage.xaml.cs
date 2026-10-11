using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using QRCoder;
using VDownloader.Services;

namespace VDownloader.Views;

public sealed partial class AccountPage : Page
{
    private CancellationTokenSource? _qrCts;

    public AccountPage()
    {
        InitializeComponent();
        BiliService.StatusChanged += () => DispatcherQueue.TryEnqueue(UpdateView);
        UpdateView();
    }

    private void UpdateView()
    {
        var st = BiliService.Status;
        bool logged = st.LoggedIn == true;
        NameText.Text = logged ? st.Name : "未登录";
        StatusText.Text = st.LoggedIn switch
        {
            true when st.Vip => $"{st.VipLabel} · 可下载最高 8K / 杜比视界 / HDR 画质",
            true => "普通账号 · 可下载 1080P / 1080P60（4K 等需要大会员）",
            null => st.Error,
            _ => string.IsNullOrEmpty(st.Error) ? "登录后可解锁更高画质" : st.Error,
        };
        VipBadge.Visibility = logged && st.Vip ? Visibility.Visible : Visibility.Collapsed;
        VipText.Text = st.VipLabel;
        LevelBadge.Visibility = logged && st.Level > 0 ? Visibility.Visible : Visibility.Collapsed;
        LevelText.Text = $"LV{st.Level}";
        LoginButton.Visibility = logged ? Visibility.Collapsed : Visibility.Visible;
        // 登录过期时本机可能还留着旧凭据，只要文件在就允许清除
        LogoutButton.Visibility = logged || File.Exists(AppPaths.BiliCookieFile) ? Visibility.Visible : Visibility.Collapsed;
        if (logged && !string.IsNullOrEmpty(st.Face) && Uri.TryCreate(st.Face, UriKind.Absolute, out var face))
            Avatar.ProfilePicture = new BitmapImage(face) { DecodePixelWidth = 144 };
        else
        {
            Avatar.ProfilePicture = null;
            Avatar.DisplayName = logged ? st.Name : "";
        }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        StatusText.Text = "正在检查…";
        await BiliService.RefreshStatusAsync();
    }

    private async void Logout_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new ContentDialog
        {
            Title = "退出 B站登录？",
            Content = "会先通知 B站服务器作废这次登录（之后即使有人拿到这份凭据也用不了），" +
                      "再删除保存在本机的登录信息。退出后下载只能获取免费画质。",
            PrimaryButtonText = "退出登录",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };
        if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;

        LogoutButton.IsEnabled = false;
        LogoutButton.Content = "正在退出…";
        try
        {
            var (revoked, message) = await BiliService.LogoutAsync();
            App.MainWindow.ShowMessage(revoked ? "已退出 B站登录" : "已删除本机登录信息", message,
                revoked ? InfoBarSeverity.Success : InfoBarSeverity.Warning, 10);
        }
        finally
        {
            LogoutButton.IsEnabled = true;
            LogoutButton.Content = "退出登录";
        }
    }

    private void Login_Click(object sender, RoutedEventArgs e) => _ = StartQrAsync();
    private void RefreshQr_Click(object sender, RoutedEventArgs e) => _ = StartQrAsync();

    private void CancelQr_Click(object sender, RoutedEventArgs e)
    {
        _qrCts?.Cancel();
        QrPanel.Visibility = Visibility.Collapsed;
    }

    protected override void OnNavigatedFrom(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _qrCts?.Cancel();
        QrPanel.Visibility = Visibility.Collapsed;
    }

    private void SetQrStatus(string message, InfoBarSeverity severity = InfoBarSeverity.Informational)
    {
        QrStatusBar.Message = message;
        QrStatusBar.Severity = severity;
    }

    private async Task StartQrAsync()
    {
        _qrCts?.Cancel();
        var cts = _qrCts = new CancellationTokenSource();
        var ct = cts.Token;

        QrPanel.Visibility = Visibility.Visible;
        QrPanel.StartBringIntoView();
        QrImage.Source = null;
        QrMask.Visibility = Visibility.Collapsed;
        QrRing.IsActive = true;
        SetQrStatus("正在获取二维码…");

        using var session = new BiliService.QrSession();
        try
        {
            var url = await session.GenerateAsync(ct);
            QrImage.Source = await RenderQrAsync(url);
            QrRing.IsActive = false;
            SetQrStatus("请用哔哩哔哩 App 扫码，二维码约 3 分钟内有效");

            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(1500, ct);
                var code = await session.PollAsync(ct);
                switch (code)
                {
                    case BiliService.QrScanned:
                        ShowMask("", "已扫码，请在手机上确认", "#0F7B0F");
                        SetQrStatus("已扫码，请在手机上点击「确认登录」", InfoBarSeverity.Success);
                        break;
                    case BiliService.QrExpired:
                        ShowMask("", "二维码已过期", "#9A6700");
                        SetQrStatus("二维码已过期，请点击「刷新二维码」", InfoBarSeverity.Warning);
                        return;
                    case BiliService.QrSuccess:
                        SetQrStatus("登录成功！", InfoBarSeverity.Success);
                        QrPanel.Visibility = Visibility.Collapsed;
                        var st = await BiliService.RefreshStatusAsync(CancellationToken.None);
                        App.MainWindow.ShowMessage("B站登录成功",
                            st.Vip ? $"欢迎，{st.Name}（{st.VipLabel}）。现在可以下载 4K / HDR / 杜比视界 / 8K 了。"
                                   : $"欢迎，{st.Name}。现在可以下载 1080P 高清了。",
                            InfoBarSeverity.Success);
                        return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 用户取消或离开页面
        }
        catch (Exception ex)
        {
            QrRing.IsActive = false;
            SetQrStatus("获取二维码失败：" + ex.Message + "（请检查网络，或在设置里关闭代理后重试）", InfoBarSeverity.Error);
        }
    }

    private void ShowMask(string glyph, string text, string color)
    {
        QrMask.Visibility = Visibility.Visible;
        QrMaskIcon.Glyph = glyph;
        QrMaskText.Text = text;
        var c = color.TrimStart('#');
        QrMaskIcon.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255,
            Convert.ToByte(c[..2], 16), Convert.ToByte(c[2..4], 16), Convert.ToByte(c[4..6], 16)));
    }

    private static async Task<BitmapImage> RenderQrAsync(string text)
    {
        using var gen = new QRCodeGenerator();
        using var data = gen.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
        var png = new PngByteQRCode(data).GetGraphic(10);
        var bmp = new BitmapImage();
        using var ms = new MemoryStream(png);
        await bmp.SetSourceAsync(ms.AsRandomAccessStream());
        return bmp;
    }
}
