namespace RyzenTunerNext.App.Services;

/// <summary>
/// 应用环境路径辅助。
/// PublishSingleFile 模式下 AppContext.BaseDirectory 指向临时解压目录，
/// 必须使用 Environment.ProcessPath 获取 exe 所在目录。
/// </summary>
internal static class AppEnvironment
{
    /// <summary>
    /// exe 文件所在目录（数据库、日志、原生 DLL 等文件应存放在此）
    /// </summary>
    public static string ExeDirectory { get; } =
        Path.GetDirectoryName(Environment.ProcessPath)
        ?? AppContext.BaseDirectory;
}
