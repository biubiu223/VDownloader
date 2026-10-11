using Microsoft.UI.Xaml;
using VDownloader.Services;

namespace VDownloader;

public partial class App : Application
{
    public static MainWindow MainWindow { get; private set; } = null!;

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) =>
        {
            Log.Error("未处理的异常：" + e.Exception);
            e.Handled = true;
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Error("后台任务异常：" + e.Exception.GetBaseException().Message);
            e.SetObserved();
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        AppSettings.Load();
        Log.Info($"万能视频下载器 v{AppInfo.Version} 启动，数据目录：{AppPaths.DataDir}");
        // 安全相关的启动清理：抹掉上次异常退出残留的临时明文 Cookie，把旧版明文登录文件转为加密保存
        SecureStore.CleanupStaleTempFiles();
        BiliService.EnsureMigrated();
        MainWindow = new MainWindow();
        MainWindow.Activate();
    }
}
