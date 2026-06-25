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
                    // 先尝试检查并安装 WinRing0 驱动
                    if (!CheckWinRing0Device().Contains("已就绪"))
                    {
                        EnsureWinRing0DriverRunning();
                        // 等待一下驱动加载
                        Thread.Sleep(500);
                    }

                    // 尝试显式加载 WinRing0x64.dll 后重试
                    var retryDiag = TryExplicitLoadAndRetry();
                    if (_handle == IntPtr.Zero)
                        return (false, $"init_ryzenadj 返回空句柄。{retryDiag}");
                }

                int tableResult = RyzenAdjNative.init_table(_handle);
                _tableInitialized = tableResult == 0;
                if (tableResult != 0)
                    return (false, $"init_table 失败: {tableResult}");

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

        var files = new[] { "libryzenadj.dll", "WinRing0x64.dll", "WinRing0x64.sys", "inpoutx64.dll" };
        var missing = files.Where(f => !File.Exists(Path.Combine(nativeDir, f))).ToArray();
        if (missing.Length > 0)
            return $"native/ 目录缺少文件: {string.Join(", ", missing)}（路径: {nativeDir}）";

        return null;
    }

    /// <summary>
    /// 尝试显式加载各 DLL 并重试 init_ryzenadj。
    /// 返回诊断信息字符串。
    /// </summary>
    private string TryExplicitLoadAndRetry()
    {
        var nativeDir = FindNativeDirectory();
        if (nativeDir == null)
            return "native/ 目录不存在，无法重试";

        var details = new System.Text.StringBuilder();

        // 1. 逐个测试 DLL 可加载性
        foreach (var dllName in new[] { "libryzenadj.dll", "WinRing0x64.dll", "inpoutx64.dll" })
        {
            var dllPath = Path.Combine(nativeDir, dllName);
            bool loaded = NativeLibrary.TryLoad(dllPath, out _);
            details.Append($"{dllName}: {(loaded ? "OK" : "FAIL")}；");
        }

        // 2. 检查 WinRing0 驱动设备是否就绪
        var deviceStatus = CheckWinRing0Device();
        details.Append(deviceStatus);

        if (deviceStatus.Contains("失败"))
        {
            // 3. 尝试安装并启动 WinRing0 内核驱动
            details.Append("尝试安装驱动...；");
            bool installed = EnsureWinRing0DriverRunning();
            details.Append($"驱动安装: {(installed ? "成功" : "失败")}；");

            if (installed)
            {
                // 重新检查设备
                var retryStatus = CheckWinRing0Device();
                details.Append($"安装后检查: {retryStatus}；");
            }
        }

        // 4. 再次设置 DLL 搜索路径确保正确
        NativeLibraryLoader.AddDllSearchPath(nativeDir);

        // 5. 重试 init_ryzenadj
        _handle = RyzenAdjNative.init_ryzenadj();
        if (_handle != IntPtr.Zero)
        {
            details.Append("；重试 init_ryzenadj 成功");
        }
        else
        {
            details.Append("；重试 init_ryzenadj 仍返回 NULL");
        }

        return details.ToString();
    }

    /// <summary>
    /// 尝试打开 WinRing0 设备句柄来判断驱动是否已加载。
    /// WinRing0 驱动加载后会创建 \\.\WinRing0_1_2_0 设备。
    /// </summary>
    private static string CheckWinRing0Device()
    {
        const string devicePath = @"\\.\WinRing0_1_2_0";
        var handle = CreateFile(devicePath, 0, FILE_SHARE_READ | FILE_SHARE_WRITE,
            IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
        if (handle == INVALID_HANDLE_VALUE)
        {
            var err = Marshal.GetLastWin32Error();
            // 2 = ERROR_FILE_NOT_FOUND（驱动未加载）
            // 5 = ERROR_ACCESS_DENIED（权限不足）
            // 32 = ERROR_SHARING_VIOLATION（驱动已被其他进程独占打开）
            return $"WinRing0 设备打开失败(lastErr={err})；";
        }
        CloseHandle(handle);
        return "WinRing0 设备已就绪；";
    }

    #region Device P/Invoke

    private const uint OPEN_EXISTING = 3;
    private const uint FILE_SHARE_READ = 1;
    private const uint FILE_SHARE_WRITE = 2;
    private static readonly IntPtr INVALID_HANDLE_VALUE = new(-1);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateFile(
        string lpFileName, uint dwDesiredAccess, uint dwShareMode,
        IntPtr lpSecurityAttributes, uint dwCreationDisposition,
        uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    #endregion

    #region SCM P/Invoke (WinRing0 驱动安装/启动)

    private const uint SC_MANAGER_ALL_ACCESS = 0xF003F;
    private const uint SERVICE_ALL_ACCESS = 0xF01FF;
    private const uint SERVICE_KERNEL_DRIVER = 0x00000001;
    private const uint SERVICE_DEMAND_START = 0x00000003;
    private const uint SERVICE_ERROR_NORMAL = 0x00000001;
    private const uint SERVICE_RUNNING = 0x00000004;
    private const uint SERVICE_START_PENDING = 0x00000002;

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr OpenSCManager(
        string? lpMachineName, string? lpDatabaseName, uint dwDesiredAccess);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateService(
        IntPtr hSCManager, string lpServiceName, string lpDisplayName,
        uint dwDesiredAccess, uint dwServiceType, uint dwStartType,
        uint dwErrorControl, string lpBinaryPathName,
        string? lpLoadOrderGroup, IntPtr lpdwTagId,
        string? lpDependencies, string? lpServiceStartName,
        string? lpPassword);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr OpenService(
        IntPtr hSCManager, string lpServiceName, uint dwDesiredAccess);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool StartService(
        IntPtr hService, uint dwNumServiceArgs, string[]? lpServiceArgVectors);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool QueryServiceStatus(
        IntPtr hService, out SERVICE_STATUS lpServiceStatus);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool DeleteService(IntPtr hService);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool CloseServiceHandle(IntPtr hSCObject);

    [StructLayout(LayoutKind.Sequential)]
    private struct SERVICE_STATUS
    {
        public uint dwServiceType;
        public uint dwCurrentState;
        public uint dwControlsAccepted;
        public uint dwWin32ExitCode;
        public uint dwServiceSpecificExitCode;
        public uint dwCheckPoint;
        public uint dwWaitHint;
    }

    #endregion

    /// <summary>
    /// 确保 WinRing0 内核驱动已安装并运行。
    /// WinRing0x64.sys 通过 SCM 注册为内核驱动服务并启动，
    /// 驱动启动后会在 \\.\WinRing0_1_2_0 创建设备，供 WinRing0x64.dll 使用。
    /// 需要在管理员权限下运行。
    /// </summary>
    private static bool EnsureWinRing0DriverRunning()
    {
        const string serviceName = "WinRing0_1_2_0";
        const string displayName = "WinRing0 Low Level Access Driver";

        var nativeDir = FindNativeDirectory();
        if (nativeDir == null) return false;

        var sysPath = Path.Combine(nativeDir, "WinRing0x64.sys");
        if (!File.Exists(sysPath)) return false;

        var scm = OpenSCManager(null, null, SC_MANAGER_ALL_ACCESS);
        if (scm == IntPtr.Zero) return false;

        try
        {
            // 尝试打开已有服务
            var service = OpenService(scm, serviceName, SERVICE_ALL_ACCESS);
            try
            {
                if (service == IntPtr.Zero)
                {
                    var lastErr = Marshal.GetLastWin32Error();
                    // 1060 = ERROR_SERVICE_DOES_NOT_EXIST
                    if (lastErr == 1060)
                    {
                        // 服务不存在，注册新的内核驱动服务
                        service = CreateService(
                            scm, serviceName, displayName,
                            SERVICE_ALL_ACCESS, SERVICE_KERNEL_DRIVER,
                            SERVICE_DEMAND_START, SERVICE_ERROR_NORMAL,
                            sysPath, null, IntPtr.Zero, null, null, null
                        );
                    }
                }

                if (service == IntPtr.Zero) return false;

                // 查询服务当前状态
                if (QueryServiceStatus(service, out var status))
                {
                    if (status.dwCurrentState == SERVICE_RUNNING)
                        return true;

                    // 如果正在启动中，等待完成
                    if (status.dwCurrentState == SERVICE_START_PENDING)
                        return WaitForServiceStart(service, 5000);
                }

                // 启动服务
                if (!StartService(service, 0, null)) return false;

                // 等待驱动加载（最多 5 秒）
                return WaitForServiceStart(service, 5000);
            }
            finally
            {
                if (service != IntPtr.Zero) CloseServiceHandle(service);
            }
        }
        finally
        {
            CloseServiceHandle(scm);
        }
    }

    /// <summary>
    /// 等待服务达到运行状态，超时则返回 false。
    /// </summary>
    private static bool WaitForServiceStart(IntPtr service, int timeoutMs)
    {
        var deadline = Environment.TickCount + timeoutMs;
        while (Environment.TickCount < deadline)
        {
            Thread.Sleep(200);
            if (QueryServiceStatus(service, out var status))
            {
                if (status.dwCurrentState == SERVICE_RUNNING) return true;
                if (status.dwCurrentState == SERVICE_STOPPED) return false;
                // SERVICE_START_PENDING: 继续等待
            }
        }
        return false;
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
            int modeErr;
            if (profile.Mode == EnergyMode.PowerSaving)
                modeErr = RyzenAdjNative.set_power_saving(_handle);
            else
                modeErr = RyzenAdjNative.set_max_performance(_handle);

            if (modeErr != 0)
                return ApplyResult.Failed(
                    $"set_power_saving/set_max_performance 失败: {modeErr}");

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
