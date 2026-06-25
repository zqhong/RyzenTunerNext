using System.Reflection;
using System.Runtime.InteropServices;

namespace RyzenTunerNext.Core.Services;

/// <summary>
/// 原生库加载器。
/// 将 libryzenadj.dll、WinRing0x64.dll、WinRing0x64.sys 放在 native/ 子目录，
/// 通过 DLL 搜索路径和自定义解析器让 .NET 能正确加载。
/// </summary>
public static class NativeLibraryLoader
{
    private static bool _initialized;

    /// <summary>
    /// 初始化原生库搜索路径。必须在首次 P/Invoke 调用之前执行。
    /// </summary>
    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;

        var nativeDir = FindNativeDirectory();
        if (nativeDir == null) return;

        // 1. 将 native/ 加入 Windows DLL 搜索路径
        //    libryzenadj.dll 依赖 WinRing0x64.dll，后者通过 Windows 默认搜索加载
        //    SetDllDirectory 会将其加入搜索链
        SetDllDirectory(nativeDir);

        // 2. 注册 .NET 原生库解析器，将 libryzenadj.dll 重定向到 native/
        //    使用 typeof().Assembly 比 Assembly.GetExecutingAssembly() 更可靠
        //    （后者在某些 JIT 内联场景下可能返回错误的程序集）
        NativeLibrary.SetDllImportResolver(
            typeof(NativeLibraryLoader).Assembly,
            (libraryName, assembly, searchPath) =>
            {
                if (libraryName == "libryzenadj.dll")
                {
                    var fullPath = Path.Combine(nativeDir, libraryName);
                    if (NativeLibrary.TryLoad(fullPath, out var handle))
                        return handle;
                }

                return IntPtr.Zero;
            });
    }

    /// <summary>
    /// 查找 native/ 子目录。
    /// PublishSingleFile 模式下 Content 文件被解压到 AppContext.BaseDirectory（临时目录），
    /// 非 SingleFile 模式下文件在 exe 同级目录。两个位置都检查。
    /// </summary>
    private static string? FindNativeDirectory()
    {
        // 1. exe 同级目录的 native/ 子目录（非 SingleFile 模式）
        var exeDir = Path.GetDirectoryName(Environment.ProcessPath);
        if (exeDir != null)
        {
            var candidate = Path.Combine(exeDir, "native");
            if (Directory.Exists(candidate))
                return candidate;
        }

        // 2. PublishSingleFile 解压目录的 native/ 子目录
        var baseDir = AppContext.BaseDirectory;
        if (!string.IsNullOrEmpty(baseDir))
        {
            var candidate = Path.Combine(baseDir, "native");
            if (Directory.Exists(candidate))
                return candidate;
        }

        return null;
    }

    /// <summary>
    /// 将指定目录加入 Windows DLL 搜索路径。
    /// 供其他模块在需要时重新设置搜索路径。
    /// </summary>
    internal static void AddDllSearchPath(string directory)
    {
        SetDllDirectory(directory);
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool SetDllDirectory(string lpPathName);
}
