namespace RyzenTunerNext.App.Services;

/// <summary>
/// 诊断用同步文件日志。
/// 用于捕获进程崩溃前的关键信息，绕过异步日志的延迟问题。
/// </summary>
internal static class DiagnosticFileLogger
{
    private static readonly string LogPath = Path.Combine(
        AppContext.BaseDirectory, "diagnostic.log");

    /// <summary>
    /// 同步写入一行日志。进程崩溃前也会刷盘。
    /// </summary>
    public static void Write(string message)
    {
        try
        {
            var line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}";
            File.AppendAllText(LogPath, line);
        }
        catch
        {
            // 日志写入失败不应影响主逻辑
        }
    }
}
