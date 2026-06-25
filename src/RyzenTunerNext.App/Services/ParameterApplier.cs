using Microsoft.Extensions.Logging;
using RyzenTunerNext.Core.Data;
using RyzenTunerNext.Core.Models;
using RyzenTunerNext.Core.Services;

namespace RyzenTunerNext.App.Services;

/// <summary>
/// 参数下发 + 验证模块
/// </summary>
public class ParameterApplier
{
    private readonly RyzenAdjWrapper _ryzenAdj;
    private readonly SettingsRepository _settings;
    private readonly LogRepository _logs;
    private readonly ILogger<ParameterApplier> _logger;

    public ParameterApplier(
        RyzenAdjWrapper ryzenAdj,
        SettingsRepository settings,
        LogRepository logs,
        ILogger<ParameterApplier> logger)
    {
        _ryzenAdj = ryzenAdj;
        _settings = settings;
        _logs = logs;
        _logger = logger;
    }

    /// <summary>
    /// 根据当前模式计算功耗配置
    /// </summary>
    public async Task<PowerProfile> GetCurrentProfileAsync()
    {
        var mode = await _settings.GetEnergyModeAsync();
        var tctlTemp = await _settings.GetTctlTempAsync();

        if (mode == "PowerSaving")
        {
            return PowerProfile.PowerSaving(
                await _settings.GetFastLimitPowersavingAsync(),
                await _settings.GetSlowLimitPowersavingAsync(),
                tctlTemp);
        }

        // Auto 和 Performance 都使用性能模式参数
        // Auto 模式由 ModeScheduler 负责切换
        return PowerProfile.Performance(
            await _settings.GetFastLimitPerformanceAsync(),
            await _settings.GetSlowLimitPerformanceAsync(),
            tctlTemp);
    }

    /// <summary>
    /// 下发参数并验证
    /// </summary>
    public async Task<ApplyResult> ApplyAndVerifyAsync(PowerProfile profile)
    {
        var result = _ryzenAdj.ApplyProfile(profile);

        if (!result.Success)
        {
            _logger.LogError("参数下发失败: {Error}", result.ErrorMessage);
            await _logs.ErrorAsync("Service", $"参数下发失败: {result.ErrorMessage}");
            return result;
        }

        // 验证: 设置值 vs 实际值
        if (result.Actual != null)
        {
            await VerifyLimitsAsync(profile, result.Actual);
        }

        return result;
    }

    private async Task VerifyLimitsAsync(PowerProfile profile, ActualValues actual)
    {
        const float toleranceW = 1000; // 1W = 1000mW

        if (Math.Abs(profile.FastLimit - actual.FastLimit) > toleranceW)
        {
            var msg = $"Fast Limit 被 BIOS cap: 设置 {profile.FastLimit / 1000}W, 实际 {actual.FastLimit / 1000:F1}W";
            _logger.LogWarning(msg);
            await _logs.WarningAsync("Service", msg);
        }

        if (Math.Abs(profile.SlowLimit - actual.SlowLimit) > toleranceW)
        {
            var msg = $"Slow Limit 被 BIOS cap: 设置 {profile.SlowLimit / 1000}W, 实际 {actual.SlowLimit / 1000:F1}W";
            _logger.LogWarning(msg);
            await _logs.WarningAsync("Service", msg);
        }
    }
}
