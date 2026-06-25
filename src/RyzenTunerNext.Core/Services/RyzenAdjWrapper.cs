using System.Runtime.InteropServices;
using RyzenTunerNext.Core.Models;

namespace RyzenTunerNext.Core.Services;

/// <summary>
/// RyzenAdj 线程安全封装。
/// ryzen_access 句柄不是线程安全的，所有操作通过 lock 串行化。
/// </summary>
public sealed class RyzenAdjWrapper : IDisposable
{
    private readonly object _lock = new();
    private IntPtr _handle;
    private bool _tableInitialized;
    private bool _disposed;

    public bool IsInitialized => _handle != IntPtr.Zero;
    public bool IsTableInitialized => _tableInitialized;

    /// <summary>
    /// 初始化 RyzenAdj 和 PM Table。
    /// 需要管理员/ SYSTEM 权限。
    /// 返回 (是否成功, 错误信息)。
    /// </summary>
    public (bool Success, string? Error) Initialize()
    {
        lock (_lock)
        {
            if (_disposed) return (false, "RyzenAdjWrapper 已释放");

            try
            {
                // 诊断：检查 native 文件是否就绪
                var diag = RunDiagnostics();
                if (diag != null)
                    return (false, $"前置检查失败: {diag}");

                _handle = RyzenAdjNative.init_ryzenadj();
                if (_handle == IntPtr.Zero)
                {
                    // init_ryzenadj 失败，可能是 WinRing0 驱动加载问题
                    // 尝试显式加载 WinRing0x64.dll 后重试
                    var retryDiag = TryExplicitLoadAndRetry();
                    if (_handle == IntPtr.Zero)
                        return (false, $"init_ryzenadj 返回空句柄。{retryDiag}");
                }

                int tableResult = RyzenAdjNative.init_table(_handle);
                _tableInitialized = tableResult == 0;
                return (true, null);
            }
            catch (DllNotFoundException ex)
            {
                return (false, $"DLL 加载失败: {ex.Message}");
            }
            catch (EntryPointNotFoundException ex)
            {
                return (false, $"DLL 入口点未找到: {ex.Message}");
            }
            catch (Exception ex)
            {
                return (false, $"初始化异常: {ex}");
            }
        }
    }

    /// <summary>
    /// 检查 native 目录下的文件是否就绪。返回 null 表示通过，否则返回诊断信息。
    /// </summary>
    private static string? RunDiagnostics()
    {
        var nativeDir = FindNativeDirectory();
        if (nativeDir == null)
            return "native/ 目录不存在";

        var files = new[] { "libryzenadj.dll", "WinRing0x64.dll", "WinRing0x64.sys" };
        var missing = files.Where(f => !File.Exists(Path.Combine(nativeDir, f))).ToArray();
        if (missing.Length > 0)
            return $"native/ 目录缺少文件: {string.Join(", ", missing)}（路径: {nativeDir}）";

        return null;
    }

    /// <summary>
    /// 尝试显式加载 WinRing0x64.dll 并重试 init_ryzenadj。
    /// 返回诊断信息字符串。
    /// </summary>
    private string TryExplicitLoadAndRetry()
    {
        var nativeDir = FindNativeDirectory();
        if (nativeDir == null)
            return "native/ 目录不存在，无法重试";

        var details = new System.Text.StringBuilder();

        // 1. 显式加载 WinRing0x64.dll
        var winringDllPath = Path.Combine(nativeDir, "WinRing0x64.dll");
        bool winringLoaded = NativeLibrary.TryLoad(winringDllPath, out var winringHandle);
        details.Append($"WinRing0x64.dll 加载: {(winringLoaded ? "成功" : "失败")}");

        // 2. 再次设置 DLL 搜索路径确保正确
        NativeLibraryLoader.AddDllSearchPath(nativeDir);

        // 3. 重试 init_ryzenadj
        _handle = RyzenAdjNative.init_ryzenadj();
        if (_handle != IntPtr.Zero)
        {
            details.Append("；重试 init_ryzenadj 成功");
        }
        else
        {
            details.Append("；重试 init_ryzenadj 仍返回 NULL");
            // 检查驱动文件详情
            var sysPath = Path.Combine(nativeDir, "WinRing0x64.sys");
            if (File.Exists(sysPath))
            {
                var fi = new FileInfo(sysPath);
                details.Append($"；WinRing0x64.sys 大小={fi.Length}B, 修改时间={fi.LastWriteTime}");
            }
        }

        return details.ToString();
    }

    /// <summary>
    /// 查找 native/ 子目录（与 NativeLibraryLoader 逻辑一致）。
    /// </summary>
    private static string? FindNativeDirectory()
    {
        var exeDir = Path.GetDirectoryName(Environment.ProcessPath);
        if (exeDir != null)
        {
            var candidate = Path.Combine(exeDir, "native");
            if (Directory.Exists(candidate)) return candidate;
        }

        var baseDir = AppContext.BaseDirectory;
        if (!string.IsNullOrEmpty(baseDir))
        {
            var candidate = Path.Combine(baseDir, "native");
            if (Directory.Exists(candidate)) return candidate;
        }

        return null;
    }

    /// <summary>
    /// 获取 CPU 系列枚举值
    /// </summary>
    public int GetCpuFamily()
    {
        lock (_lock)
        {
            if (_disposed || _handle == IntPtr.Zero) return -1;
            return RyzenAdjNative.get_cpu_family(_handle);
        }
    }

    /// <summary>
    /// 应用功耗模式并验证实际值。
    /// 返回 (set 是否成功, PM Table 读回的实际值)。
    /// </summary>
    public ApplyResult ApplyProfile(PowerProfile profile)
    {
        lock (_lock)
        {
            if (_disposed || _handle == IntPtr.Zero)
                return ApplyResult.Failed("RyzenAdj 未初始化");

            // 1. 设置 power mode flag
            if (profile.Mode == EnergyMode.PowerSaving)
                RyzenAdjNative.set_power_saving(_handle);
            else
                RyzenAdjNative.set_max_performance(_handle);

            // 2. 设置核心参数
            int err;
            err = RyzenAdjNative.set_fast_limit(_handle, (uint)profile.FastLimit);
            if (err != 0) return ApplyResult.Failed($"set_fast_limit 失败: {err}");

            err = RyzenAdjNative.set_slow_limit(_handle, (uint)profile.SlowLimit);
            if (err != 0) return ApplyResult.Failed($"set_slow_limit 失败: {err}");

            // stapm-limit = slow-limit
            err = RyzenAdjNative.set_stapm_limit(_handle, (uint)profile.SlowLimit);
            if (err != 0) return ApplyResult.Failed($"set_stapm_limit 失败: {err}");

            err = RyzenAdjNative.set_tctl_temp(_handle, (uint)profile.TctlTemp);
            if (err != 0) return ApplyResult.Failed($"set_tctl_temp 失败: {err}");

            // 3. 验证: 刷新 PM Table 读回实际值
            var actual = ReadActualValues();
            return ApplyResult.SuccessResult(actual);
        }
    }

    /// <summary>
    /// 读取 PM Table 中的实际生效值。
    /// SMU 可能返回成功但值被 BIOS cap 截断，必须验证。
    /// </summary>
    public ActualValues ReadActualValues()
    {
        lock (_lock)
        {
            if (!_tableInitialized || _handle == IntPtr.Zero)
                return ActualValues.Empty;

            RyzenAdjNative.refresh_table(_handle);

            return new ActualValues
            {
                FastLimit = RyzenAdjNative.get_fast_limit(_handle),
                FastValue = RyzenAdjNative.get_fast_value(_handle),
                SlowLimit = RyzenAdjNative.get_slow_limit(_handle),
                SlowValue = RyzenAdjNative.get_slow_value(_handle),
                StapmLimit = RyzenAdjNative.get_stapm_limit(_handle),
                StapmValue = RyzenAdjNative.get_stapm_value(_handle),
                TctlTemp = RyzenAdjNative.get_tctl_temp(_handle),
                TctlTempValue = RyzenAdjNative.get_tctl_temp_value(_handle),
                SocketPower = RyzenAdjNative.get_socket_power(_handle),
            };
        }
    }

    /// <summary>
    /// 读取 CPU 实时指标
    /// </summary>
    public CpuMetrics ReadCpuMetrics(uint coreCount = 16)
    {
        lock (_lock)
        {
            if (!_tableInitialized || _handle == IntPtr.Zero)
                return new CpuMetrics();

            RyzenAdjNative.refresh_table(_handle);

            float totalFreq = 0;
            float maxTemp = 0;
            int validCores = 0;

            for (uint i = 0; i < coreCount; i++)
            {
                float clk = RyzenAdjNative.get_core_clk(_handle, i);
                float temp = RyzenAdjNative.get_core_temp(_handle, i);
                if (clk > 0)
                {
                    totalFreq += clk;
                    validCores++;
                }
                if (temp > maxTemp) maxTemp = temp;
            }

            return new CpuMetrics
            {
                AvgFrequency = validCores > 0 ? totalFreq / validCores : 0,
                SocketPower = RyzenAdjNative.get_socket_power(_handle),
                CpuTemp = maxTemp > 0 ? maxTemp : RyzenAdjNative.get_tctl_temp_value(_handle),
            };
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (!_disposed && _handle != IntPtr.Zero)
            {
                RyzenAdjNative.cleanup_ryzenadj(_handle);
                _handle = IntPtr.Zero;
                _tableInitialized = false;
            }
            _disposed = true;
        }
    }
}
