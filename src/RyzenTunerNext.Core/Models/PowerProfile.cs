namespace RyzenTunerNext.Core.Models;

/// <summary>
/// 功耗配置 - 用于下发到 RyzenAdj 的一组参数
/// </summary>
public class PowerProfile
{
    public EnergyMode Mode { get; init; } = EnergyMode.Performance;

    /// <summary>PPT FAST 瞬时功耗上限 (mW)</summary>
    public int FastLimit { get; init; }

    /// <summary>PPT SLOW / STAPM 持续功耗上限 (mW)</summary>
    public int SlowLimit { get; init; }

    /// <summary>Tctl 温度目标 (°C)</summary>
    public int TctlTemp { get; init; } = 90;

    public static PowerProfile Performance(int fastLimit, int slowLimit, int tctlTemp) => new()
    {
        Mode = EnergyMode.Performance,
        FastLimit = fastLimit,
        SlowLimit = slowLimit,
        TctlTemp = tctlTemp
    };

    public static PowerProfile PowerSaving(int fastLimit, int slowLimit, int tctlTemp) => new()
    {
        Mode = EnergyMode.PowerSaving,
        FastLimit = fastLimit,
        SlowLimit = slowLimit,
        TctlTemp = tctlTemp
    };
}
