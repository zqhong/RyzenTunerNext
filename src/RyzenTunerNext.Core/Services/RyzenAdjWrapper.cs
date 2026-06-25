using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;
using System.Text;
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

    // =====================================================================
    //  Initialize — 含精确诊断的重试流程
    // =====================================================================

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
                // ===== 第 1 步：前置检查 =====

                // 确保 DLL 搜索路径包含 native/ 子目录
                var nativeDir = FindNativeDirectory();
                if (nativeDir != null)
                    NativeLibraryLoader.AddDllSearchPath(nativeDir);

                // 1a) DLL 文件存在
                var diag = RunDiagnostics();
                if (diag != null)
                    return (false, $"前置检查失败: {diag}");

                // 1b) CPU 兼容性检测（无需管理员权限即可执行）
                var cpuInfo = GetCpuInfo();
                if (!cpuInfo.Supported)
                {
                    return (false, $"CPU 不被 RyzenAdj v0.19.0 支持。CPU: {cpuInfo.Name}(Family=0x{cpuInfo.Family:X}, Model=0x{cpuInfo.Model:X})。需要更新 libryzenadj.dll 以支持该 CPU");
                }

                // 1c) WinRing0 OLS 初始化状态
                var olsStatus = CheckOlsStatus();
                if (!olsStatus.Contains("已就绪"))
                {
                    // OLS 未就绪，先尝试安装/启动 WinRing0 内核驱动再重试
                    if (!CheckWinRing0Device().Contains("已就绪"))
                    {
                        EnsureWinRing0DriverRunning();
                        Thread.Sleep(500);
                    }

                    olsStatus = CheckOlsStatus();
                    if (!olsStatus.Contains("已就绪"))
                    {
                        return (false, $"WinRing0 OLS 初始化失败: {olsStatus}");
                    }
                }

                // ===== 第 2 步：调用 init_ryzenadj =====
                _handle = RyzenAdjNative.init_ryzenadj();
                if (_handle == IntPtr.Zero)
                {
                    if (!CheckWinRing0Device().Contains("已就绪"))
                    {
                        EnsureWinRing0DriverRunning();
                        Thread.Sleep(500);
                    }

                    // 接管 stderr 以捕获 libryzenadj 的诊断输出
                    (_handle, var stderrOutput) = CaptureStderr(() => RyzenAdjNative.init_ryzenadj());

                    if (_handle == IntPtr.Zero)
                    {
                        var lines = stderrOutput.Trim();
                        var sb = new StringBuilder();
                        sb.Append("init_ryzenadj 返回空句柄。");
                        if (lines.Length > 0)
                            sb.Append($"libryzenadj 诊断: {lines}。");
                        sb.Append($"CPU: {cpuInfo.Name}(F={cpuInfo.Family:X} M={cpuInfo.Model:X})。");
                        sb.Append(olsStatus);
                        sb.Append(CheckWinRing0Device());

                        // 根据 stderr 输出给出引导
                        if (lines.Contains("PCI Bus is not writeable"))
                            sb.Append("PCI 配置空间不可写，请检查：Secure Boot/VBS/Hyper-V 是否开启？");
                        else if (lines.Contains("Unable to get os_access"))
                            sb.Append("WinRing0 初始化失败，请以管理员权限运行。");
                        else if (lines.Contains("Unable to get") || lines.Contains("Failed to get SMU"))
                            sb.Append("SMU 检测失败，可能原因：不支持的 CPU 型号或 BIOS 限制。");

                        return (false, sb.ToString());
                    }
                }

                // ===== 第 3 步：init_table =====
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

    // =====================================================================
    //  CPUID 检测
    // =====================================================================

    /// <summary>
    /// CPU 检测结果
    /// </summary>
    public sealed record CpuInfo(
        string Vendor, int Family, int Model, string Name, bool Supported);

    /// <summary>
    /// 通过 CPUID 指令读取 CPU 信息（无需管理员权限）。
    /// </summary>
    private static CpuInfo GetCpuInfo()
    {
        const string expectedVendor = "AuthenticAMD";

        if (!X86Base.IsSupported)
            return new CpuInfo("X86Base 不可用", 0, 0, "未知", false);

        // CPUID 叶 0：获取厂商字符串 — EBX[0:3] + EDX[4:7] + ECX[8:11]
        var eax0 = X86Base.CpuId(0, 0);
        var vendor = string.Concat(
            (char)(eax0.Ebx & 0xFF), (char)((eax0.Ebx >> 8) & 0xFF),
            (char)((eax0.Ebx >> 16) & 0xFF), (char)((eax0.Ebx >> 24) & 0xFF),
            (char)(eax0.Edx & 0xFF), (char)((eax0.Edx >> 8) & 0xFF),
            (char)((eax0.Edx >> 16) & 0xFF), (char)((eax0.Edx >> 24) & 0xFF),
            (char)(eax0.Ecx & 0xFF), (char)((eax0.Ecx >> 8) & 0xFF),
            (char)((eax0.Ecx >> 16) & 0xFF), (char)((eax0.Ecx >> 24) & 0xFF));

        // CPUID 叶 1：解析 Family / Model
        var eax1 = X86Base.CpuId(1, 0);
        int family = ((eax1.Eax >> 8) & 0xF) + ((eax1.Eax >> 20) & 0xFF);
        int model = ((eax1.Eax >> 4) & 0xF) | ((eax1.Eax >> 12) & 0xF0);

        var vendorClean = vendor.TrimEnd('\0');
        bool isAmd = vendorClean == expectedVendor;
        bool supported = isAmd && IsCpuFamilySupported(family, model);

        return new CpuInfo(vendorClean, family, model,
            $"AMD Family 0x{family:X} Model 0x{model:X}", supported);
    }

    /// <summary>
    /// 判断给定 Family/Model 是否在 RyzenAdj v0.19.0 的支持列表中。
    /// </summary>
    private static bool IsCpuFamilySupported(int family, int model)
    {
        // 匹配 RyzenAdj v0.19.0 lib/cpuid.c 的 cpuid_load_family() 逻辑
        switch (family)
        {
            case 0x17: // Zen / Zen+ / Zen2
                return model switch
                {
                    17 => true,     // Raven
                    24 => true,     // Picasso
                    32 => true,     // Dali
                    96 => true,     // Renoir
                    104 => true,    // Lucienne
                    144 or 145 => true, // Vangogh
                    160 => true,    // Mendocino
                    _ => false,
                };
            case 0x19: // Zen3 / Zen4
                return model switch
                {
                    80 => true,     // Cezanne
                    64 or 68 => true,   // Rembrandt
                    97 => true,     // DragonRange
                    116 or 120 => true, // Phoenix
                    117 => true,    // HawkPoint
                    _ => false,
                };
            case 0x1A: // Zen5 / Zen6
                return model switch
                {
                    32 or 36 => true,   // StrixPoint
                    68 => true,         // FireRange
                    96 => true,         // KrackanPoint
                    112 => true,        // StrixHalo
                    _ => false,
                };
            default:
                return false;
        }
    }

    // =====================================================================
    //  WinRing0 OLS 状态诊断
    // =====================================================================

    /// <summary>
    /// OLS_DLL 状态码，定义见 WinRing0 OlsDef.h
    /// </summary>
    private static string DescribeOlsStatus(uint code)
    {
        return code switch
        {
            0 => "已就绪",
            1 => "不支持的平台",
            2 => "驱动未加载",
            3 => "驱动未找到",
            4 => "驱动已卸载",
            5 => "网络环境驱动未加载",
            _ => $"未知错误(code={code})",
        };
    }

    /// <summary>
    /// 直接调用 WinRing0x64.dll 的 InitializeOls/GetDllStatus 检查 OLS 初始化状态。
    /// </summary>
    private static string CheckOlsStatus()
    {
        if (!InitializeOls())
        {
            var code = GetDllStatus();
            return $"OLS 初始化失败(code={code}): {DescribeOlsStatus(code)}";
        }

        // 再次确认状态
        var finalCode = GetDllStatus();
        return $"OLS 已就绪(code={finalCode})";
    }

    // =====================================================================
    //  stderr 截获
    // =====================================================================

    private const int StdErrorHandle = -12;

    /// <summary>
    /// 执行 action 期间重定向 CRT stderr 到匿名管道并读取输出。
    /// 返回 (action 返回值, stderr 捕获内容)。
    /// 适用于截获 libryzenadj 的 fprintf(stderr, ...) 诊断消息。
    /// 原理：UCRT _write(fd=2) 每次会调用 GetStdHandle(STD_ERROR_HANDLE)，
    /// 因此提前 SetStdHandle 即可生效。
    /// </summary>
    private static (IntPtr Result, string Stderr) CaptureStderr(Func<IntPtr> action)
    {
        IntPtr hRead = IntPtr.Zero, hWrite = IntPtr.Zero;
        var oldStderr = GetStdHandle(StdErrorHandle);

        try
        {
            // 创建匿名管道（SECURITY_ATTRIBUTES = null → 句柄不可继承）
            if (!CreatePipe(out hRead, out hWrite, IntPtr.Zero, 0))
                return (action(), "(无法创建管道)");

            SetStdHandle(StdErrorHandle, hWrite);

            var result = action();

            // 立即恢复 stderr 并关闭写端，让 ReadFile 读到 EOF
            SetStdHandle(StdErrorHandle, oldStderr);
            oldStderr = IntPtr.Zero; // 防止 finally 重复恢复

            CloseHandle(hWrite);
            hWrite = IntPtr.Zero;

            // 读取管道中的全部数据
            var sb = new StringBuilder();
            var buf = new byte[4096];
            while (ReadFile(hRead, buf, buf.Length, out var read, IntPtr.Zero) && read > 0)
                sb.Append(Encoding.UTF8.GetString(buf, 0, read));

            return (result, sb.ToString());
        }
        finally
        {
            // 兜底清理：异常路径或提前返回时确保句柄和 stderr 一致
            if (oldStderr != IntPtr.Zero)
                SetStdHandle(StdErrorHandle, oldStderr);
            if (hWrite != IntPtr.Zero) CloseHandle(hWrite);
            if (hRead != IntPtr.Zero) CloseHandle(hRead);
        }
    }

    // =====================================================================
    //  现存诊断方法
    // =====================================================================

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
    /// 尝试打开 WinRing0 设备句柄来判断驱动是否已加载。
    /// </summary>
    private static string CheckWinRing0Device()
    {
        const string devicePath = @"\\.\WinRing0_1_2_0";
        var handle = CreateFile(devicePath, 0, FILE_SHARE_READ | FILE_SHARE_WRITE,
            IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
        if (handle == INVALID_HANDLE_VALUE)
        {
            var err = Marshal.GetLastWin32Error();
            return $"WinRing0 设备打开失败(lastErr={err})；";
        }
        CloseHandle(handle);
        return "WinRing0 设备已就绪；";
    }

    // =====================================================================
    //  驱动安装／SCM
    // =====================================================================

    /// <summary>
    /// 确保 WinRing0 内核驱动已安装并运行。
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
            var service = OpenService(scm, serviceName, SERVICE_ALL_ACCESS);
            try
            {
                if (service == IntPtr.Zero)
                {
                    var lastErr = Marshal.GetLastWin32Error();
                    if (lastErr == 1060)
                    {
                        service = CreateService(
                            scm, serviceName, displayName,
                            SERVICE_ALL_ACCESS, SERVICE_KERNEL_DRIVER,
                            SERVICE_DEMAND_START, SERVICE_ERROR_NORMAL,
                            sysPath, null, IntPtr.Zero, null, null, null);
                    }
                }

                if (service == IntPtr.Zero) return false;

                if (QueryServiceStatus(service, out var status))
                {
                    if (status.dwCurrentState == SERVICE_RUNNING)
                        return true;
                    if (status.dwCurrentState == SERVICE_START_PENDING)
                        return WaitForServiceStart(service, 5000);
                }

                if (!StartService(service, 0, null)) return false;
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

    // =====================================================================
    //  RyzenAdj 功能调用
    // =====================================================================

    public int GetCpuFamily()
    {
        lock (_lock)
        {
            if (_disposed || _handle == IntPtr.Zero) return -1;
            return RyzenAdjNative.get_cpu_family(_handle);
        }
    }

    public ApplyResult ApplyProfile(PowerProfile profile)
    {
        lock (_lock)
        {
            if (_disposed || _handle == IntPtr.Zero)
                return ApplyResult.Failed("RyzenAdj 未初始化");

            int modeErr;
            if (profile.Mode == EnergyMode.PowerSaving)
                modeErr = RyzenAdjNative.set_power_saving(_handle);
            else
                modeErr = RyzenAdjNative.set_max_performance(_handle);

            if (modeErr != 0)
                return ApplyResult.Failed($"set_power_saving/set_max_performance 失败: {modeErr}");

            int err;
            err = RyzenAdjNative.set_fast_limit(_handle, (uint)profile.FastLimit);
            if (err != 0) return ApplyResult.Failed($"set_fast_limit 失败: {err}");

            err = RyzenAdjNative.set_slow_limit(_handle, (uint)profile.SlowLimit);
            if (err != 0) return ApplyResult.Failed($"set_slow_limit 失败: {err}");

            err = RyzenAdjNative.set_stapm_limit(_handle, (uint)profile.SlowLimit);
            if (err != 0) return ApplyResult.Failed($"set_stapm_limit 失败: {err}");

            err = RyzenAdjNative.set_tctl_temp(_handle, (uint)profile.TctlTemp);
            if (err != 0) return ApplyResult.Failed($"set_tctl_temp 失败: {err}");

            var actual = ReadActualValues();
            return ApplyResult.SuccessResult(actual);
        }
    }

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

    // =====================================================================
    //  P/Invoke 声明区
    // =====================================================================

    #region WinRing0 OLS

    // WinRing0x64.dll 使用 WINAPI (__stdcall) 调用约定
    [DllImport("WinRing0x64.dll", CallingConvention = CallingConvention.Winapi)]
    private static extern bool InitializeOls();

    [DllImport("WinRing0x64.dll", CallingConvention = CallingConvention.Winapi)]
    private static extern uint GetDllStatus();

    #endregion

    #region stderr 管道

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CreatePipe(out IntPtr hReadPipe, out IntPtr hWritePipe,
        IntPtr lpPipeAttributes, int nSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetStdHandle(int nStdHandle, IntPtr hHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadFile(IntPtr hFile, byte[] lpBuffer,
        int nNumberOfBytesToRead, out int lpNumberOfBytesRead, IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    #endregion

    #region WinRing0 设备

    private const uint OPEN_EXISTING = 3;
    private const uint FILE_SHARE_READ = 1;
    private const uint FILE_SHARE_WRITE = 2;
    private static readonly IntPtr INVALID_HANDLE_VALUE = new(-1);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateFile(string lpFileName, uint dwDesiredAccess,
        uint dwShareMode, IntPtr lpSecurityAttributes, uint dwCreationDisposition,
        uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    #endregion

    #region SCM (WinRing0 驱动安装)

    private const uint SC_MANAGER_ALL_ACCESS = 0xF003F;
    private const uint SERVICE_ALL_ACCESS = 0xF01FF;
    private const uint SERVICE_KERNEL_DRIVER = 0x00000001;
    private const uint SERVICE_DEMAND_START = 0x00000003;
    private const uint SERVICE_ERROR_NORMAL = 0x00000001;
    private const uint SERVICE_STOPPED = 0x00000001;
    private const uint SERVICE_START_PENDING = 0x00000002;
    private const uint SERVICE_RUNNING = 0x00000004;

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
}
