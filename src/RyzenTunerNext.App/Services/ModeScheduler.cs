using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using RyzenTunerNext.Core.Data;
using RyzenTunerNext.Core.Models;

namespace RyzenTunerNext.App.Services;

/// <summary>
/// 自动模式状态机。
/// 根据用户活动 + CPU 负载判断是否切换模式。
/// </summary>
public class ModeScheduler
{
    private readonly SettingsRepository _settings;
    private readonly LogRepository _logs;
    private readonly ILogger<ModeScheduler> _logger;

    // Win32 API
    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    // 状态机
    private enum AutoState { Performance, CandidateSaving }

    private AutoState _state = AutoState.Performance;
    private DateTime _lastSwitchTime = DateTime.MinValue;
    private DateTime _candidateSince = DateTime.MinValue;
    private readonly Queue<(DateTime Time, int Load)> _cpuSamples = new();
    private static readonly TimeSpan SampleWindow = TimeSpan.FromMinutes(1);

    public ModeScheduler(
        SettingsRepository settings,
        LogRepository logs,
        ILogger<ModeScheduler> logger)
    {
        _settings = settings;
        _logs = logs;
        _logger = logger;
    }

    /// <summary>
    /// 评估当前状态，返回应该使用的模式。
    /// 仅在 Auto 模式下调用。
    /// </summary>
    public async Task<EnergyMode> EvaluateAsync()
    {
        var mode = await _settings.GetEnergyModeAsync();
        if (mode != "Auto")
            return Enum.Parse<EnergyMode>(mode);

        var idleTimeout = TimeSpan.FromMilliseconds(await _settings.GetAutoIdleTimeoutAsync());
        var cpuThreshold = await _settings.GetAutoCpuThresholdAsync();
        var cooldown = TimeSpan.FromMinutes(10);
        var debounce = TimeSpan.FromMinutes(1);

        // 采样 CPU 负载
        SampleCpuLoad();

        // 检查用户输入
        var idleTime = GetIdleTime();
        bool userActive = idleTime < idleTimeout;

        // 检查 CPU 负载
        bool cpuLow = IsCpuSustainedBelow(cpuThreshold);

        // 冷却期检查
        if (DateTime.Now - _lastSwitchTime < cooldown)
            return _state == AutoState.Performance ? EnergyMode.Performance : EnergyMode.PowerSaving;

        switch (_state)
        {
            case AutoState.Performance:
                // 切换条件: 无输入 ≥ 5min 且 CPU ≤ 10% 持续 5min
                if (!userActive && cpuLow)
                {
                    if (_candidateSince == DateTime.MinValue)
                    {
                        _candidateSince = DateTime.Now;
                        _logger.LogInformation("进入候选省电状态");
                    }
                    else if (DateTime.Now - _candidateSince >= debounce)
                    {
                        _state = AutoState.CandidateSaving;
                        _lastSwitchTime = DateTime.Now;
                        _candidateSince = DateTime.MinValue;
                        _logger.LogInformation("自动切换到省电模式");
                        await _logs.InfoAsync("AutoMode", "自动切换到省电模式");
                        return EnergyMode.PowerSaving;
                    }
                }
                else
                {
                    _candidateSince = DateTime.MinValue;
                }
                return EnergyMode.Performance;

            case AutoState.CandidateSaving:
                // 切回条件: 有输入 或 CPU > 10% → 立即切换
                if (userActive || !cpuLow)
                {
                    _state = AutoState.Performance;
                    _lastSwitchTime = DateTime.Now;
                    _logger.LogInformation("自动切换到性能模式");
                    await _logs.InfoAsync("AutoMode", "自动切换到性能模式");
                    return EnergyMode.Performance;
                }
                return EnergyMode.PowerSaving;

            default:
                return EnergyMode.Performance;
        }
    }

    /// <summary>
    /// 获取系统空闲时间
    /// </summary>
    private static TimeSpan GetIdleTime()
    {
        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        GetLastInputInfo(ref info);
        return TimeSpan.FromMilliseconds(Environment.TickCount - info.dwTime);
    }

    /// <summary>
    /// CPU 负载采样
    /// </summary>
    private void SampleCpuLoad()
    {
        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher(
                "SELECT LoadPercentage FROM Win32_Processor");
            foreach (var obj in searcher.Get())
            {
                int load = Convert.ToInt32(obj["LoadPercentage"]);
                _cpuSamples.Enqueue((DateTime.Now, load));
            }

            // 清理过期采样
            var cutoff = DateTime.Now - SampleWindow;
            while (_cpuSamples.Count > 0 && _cpuSamples.Peek().Time < cutoff)
                _cpuSamples.Dequeue();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "CPU 负载采样失败");
        }
    }

    /// <summary>
    /// 最近 1 分钟内所有采样是否都 ≤ 阈值
    /// </summary>
    private bool IsCpuSustainedBelow(int threshold)
    {
        if (_cpuSamples.Count < 6) return false; // 至少 30 秒数据
        return _cpuSamples.All(s => s.Load <= threshold);
    }

    /// <summary>
    /// 重置状态（模式从 Auto 切换到手动时调用）
    /// </summary>
    public void Reset()
    {
        _state = AutoState.Performance;
        _candidateSince = DateTime.MinValue;
    }
}
