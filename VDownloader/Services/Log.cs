using System.Text;

namespace VDownloader.Services;

/// <summary>全局运行日志：内存里保留最近的记录给“运行日志”页面显示，同时写入 app.log。</summary>
public static class Log
{
    private const int MaxLines = 3000;
    private static readonly LinkedList<string> Lines = new();
    private static readonly object Gate = new();
    private static StreamWriter? _file;

    /// <summary>有新日志时触发（可能在后台线程）。</summary>
    public static event Action? Changed;

    static Log()
    {
        try
        {
            if (File.Exists(AppPaths.LogFile) && new FileInfo(AppPaths.LogFile).Length > 2 * 1024 * 1024)
                File.Delete(AppPaths.LogFile);
            _file = new StreamWriter(AppPaths.LogFile, append: true, new UTF8Encoding(false)) { AutoFlush = true };
        }
        catch { _file = null; }
    }

    public static void Info(string msg) => Write("信息", msg);
    public static void Warn(string msg) => Write("警告", msg);
    public static void Error(string msg) => Write("错误", msg);

    public static void Write(string level, string msg)
    {
        var line = $"{DateTime.Now:HH:mm:ss} [{level}] {msg}";
        lock (Gate)
        {
            Lines.AddLast(line);
            while (Lines.Count > MaxLines) Lines.RemoveFirst();
            try { _file?.WriteLine($"{DateTime.Now:yyyy-MM-dd} {line}"); } catch { }
        }
        Changed?.Invoke();
    }

    public static string Snapshot()
    {
        lock (Gate) return string.Join(Environment.NewLine, Lines);
    }

    public static void Clear()
    {
        lock (Gate) Lines.Clear();
        Changed?.Invoke();
    }
}
