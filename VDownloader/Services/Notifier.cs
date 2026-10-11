using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace VDownloader.Services;

/// <summary>Windows 系统通知（下载完成提醒）。注册失败时静默降级。</summary>
public static class Notifier
{
    private static bool _ok;

    public static event Action? Invoked;

    public static void Init()
    {
        try
        {
            AppNotificationManager.Default.NotificationInvoked += (_, _) => Invoked?.Invoke();
            AppNotificationManager.Default.Register();
            _ok = true;
        }
        catch (Exception e)
        {
            Log.Warn("系统通知不可用：" + e.Message);
        }
    }

    public static void Show(string title, string body)
    {
        if (!_ok || !AppSettings.Current.Notify) return;
        try
        {
            var n = new AppNotificationBuilder().AddText(title).AddText(body).BuildNotification();
            AppNotificationManager.Default.Show(n);
        }
        catch (Exception e)
        {
            Log.Warn("发送通知失败：" + e.Message);
        }
    }

    public static void Shutdown()
    {
        if (!_ok) return;
        try { AppNotificationManager.Default.Unregister(); } catch { }
    }
}
