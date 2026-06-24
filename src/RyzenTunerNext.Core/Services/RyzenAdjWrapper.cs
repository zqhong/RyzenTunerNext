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
    /// </summary>
    public bool Initialize()
    {
        lock (_lock)
        {
            if (_disposed) return false;

            _handle = RyzenAdjNative.init_ryzenadj();
            if (_handle == IntPtr.Zero) return false;

            int tableResult = RyzenAdjNative.init_table(_handle);
            _tableInitialized = tableResult == 0;
            return true;
        }
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
