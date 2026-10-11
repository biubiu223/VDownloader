using System.Security.Cryptography;
using System.Text;

namespace VDownloader.Services;

/// <summary>
/// 敏感数据（B站登录 Cookie）的加密保存与安全删除。
/// 加密使用 Windows DPAPI（当前用户范围）：密钥由 Windows 根据当前账户自动管理，
/// 文件被拷到其他电脑、或在本机其他 Windows 账户下都无法解密。
/// </summary>
public static class SecureStore
{
    /// <summary>文件头，用来识别格式和版本。</summary>
    private static readonly byte[] Magic = "VDC1"u8.ToArray();

    /// <summary>附加熵：其他程序即使以你的身份调用 DPAPI，不知道它也解不开这份数据。</summary>
    private static readonly byte[] Entropy = "VDownloader/bilibili-cookies/v1"u8.ToArray();

    /// <summary>yt-dlp 只能读明文文件，下载时临时解密出来的副本都以这个前缀命名。</summary>
    public const string TempPrefix = "vdl_cookie_";

    /// <summary>加密后原子写入（先写临时文件再替换，避免写一半断电导致文件损坏）。</summary>
    public static void WriteEncrypted(string path, string text)
    {
        var plain = Encoding.UTF8.GetBytes(text);
        try
        {
            var blob = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);
            var tmp = path + ".tmp";
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                fs.Write(Magic);
                fs.Write(blob);
                fs.Flush(true);
            }
            File.Move(tmp, path, overwrite: true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    /// <summary>
    /// 解密。文件被篡改、或来自其他电脑 / 其他 Windows 账户时抛出 <see cref="CryptographicException"/>。
    /// </summary>
    public static string ReadEncrypted(string path)
    {
        var data = File.ReadAllBytes(path);
        if (data.Length <= Magic.Length || !data.AsSpan(0, Magic.Length).SequenceEqual(Magic))
            throw new CryptographicException("不是有效的加密登录文件");
        var plain = ProtectedData.Unprotect(data[Magic.Length..], Entropy, DataProtectionScope.CurrentUser);
        try { return Encoding.UTF8.GetString(plain); }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }

    /// <summary>
    /// 先用 0 覆盖文件内容再删除，降低被数据恢复软件找回的可能。
    /// （SSD 的磨损均衡可能让旧数据残留在闪存里，所以这只是“尽力而为”，真正的保护靠加密。）
    /// </summary>
    public static void SecureDelete(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
        try
        {
            File.SetAttributes(path, FileAttributes.Normal);
            var len = new FileInfo(path).Length;
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None);
            var zeros = new byte[(int)Math.Min(Math.Max(len, 1), 64 * 1024)];
            for (long done = 0; done < len; done += zeros.Length)
                fs.Write(zeros, 0, (int)Math.Min(zeros.Length, len - done));
            fs.Flush(true);
        }
        catch
        {
            // 覆盖失败（比如文件被占用）也要继续尝试删除
        }
        try { File.Delete(path); }
        catch (Exception e) { Log.Warn($"删除敏感文件失败：{Path.GetFileName(path)}：{e.Message}"); }
    }

    /// <summary>把明文写到用户临时目录（只有当前 Windows 账户可访问），用完必须调用 <see cref="SecureDelete"/>。</summary>
    public static string WriteTempPlaintext(string text)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{TempPrefix}{Guid.NewGuid():N}.txt");
        File.WriteAllText(path, text, new UTF8Encoding(false));
        // 注意：不能设“隐藏”属性。yt-dlp 退出时会把 Cookie 写回这个文件（覆盖创建），
        // 而 Windows 不允许覆盖创建隐藏文件，会导致 yt-dlp 报 PermissionError 并以失败退出。
        try { File.SetAttributes(path, FileAttributes.Temporary | FileAttributes.NotContentIndexed); }
        catch { }
        return path;
    }

    /// <summary>清理上次异常退出时残留的临时明文副本（程序启动时调用）。</summary>
    public static void CleanupStaleTempFiles()
    {
        try
        {
            foreach (var f in Directory.GetFiles(Path.GetTempPath(), TempPrefix + "*"))
                SecureDelete(f);
        }
        catch { }
    }
}
