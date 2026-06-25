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

        var baseDir = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
        var nativeDir = Path.Combine(baseDir, "native");
        if (!Directory.Exists(nativeDir)) return;

        // 1. 将 native/ 加入 Windows DLL 搜索路径
        //    libryzenadj.dll 依赖 WinRing0x64.dll，后者通过 Windows 默认搜索加载
        //    SetDllDirectory 会将其加入搜索链
        SetDllDirectory(nativeDir);

        // 2. 注册 .NET 原生库解析器，将 libryzenadj.dll 重定向到 native/
        NativeLibrary.SetDllImportResolver(
            Assembly.GetExecutingAssembly(),
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

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool SetDllDirectory(string lpPathName);
}
