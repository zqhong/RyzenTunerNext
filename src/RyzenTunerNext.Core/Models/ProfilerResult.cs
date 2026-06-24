namespace RyzenTunerNext.Core.Models;

/// <summary>
/// 能效分析结果
/// </summary>
public class ProfilerResult
{
    public long Id { get; init; }
    public string CreatedAt { get; init; } = string.Empty;
    public string TestType { get; init; } = string.Empty;
    public double Score { get; init; }
    public int FastLimit { get; init; }
    public int SlowLimit { get; init; }
    public int TctlTemp { get; init; }
    public double? AvgFrequency { get; init; }
    public double? AvgPower { get; init; }
    public double? MaxTemp { get; init; }
    public double? Efficiency { get; init; }

    // 展示用属性（带单位格式化）
    public string FastLimitDisplay => $"{FastLimit / 1000.0:F0} W";
    public string ScoreDisplay => $"{Score:N0}";
    public string AvgFrequencyDisplay => AvgFrequency.HasValue ? $"{AvgFrequency.Value:F0} MHz" : "--";
    public string AvgPowerDisplay => AvgPower.HasValue ? $"{AvgPower.Value / 1000.0:F1} W" : "--";
    public string MaxTempDisplay => MaxTemp.HasValue ? $"{MaxTemp.Value:F0} ℃" : "--";
    public string EfficiencyDisplay => Efficiency.HasValue ? $"{Efficiency.Value:F0} Pts/W" : "--";
}
