namespace RyzenTunerNext.Core.Models;

/// <summary>
/// 能效分析结果
/// </summary>
public class ProfilerResult
{
    public long Id { get; set; }
    public string CreatedAt { get; set; } = string.Empty;
    public string TestType { get; set; } = string.Empty;
    public double Score { get; set; }
    public int FastLimit { get; set; }
    public int SlowLimit { get; set; }
    public int TctlTemp { get; set; }
    public double? AvgFrequency { get; set; }
    public double? AvgPower { get; set; }
    public double? MaxTemp { get; set; }
    public double? Efficiency { get; set; }

    // 展示用属性（带单位格式化）
    public string FastLimitDisplay => $"{FastLimit / 1000.0:F0} W";
    public string ScoreDisplay => $"{Score:N0}";
    public string AvgFrequencyDisplay => AvgFrequency.HasValue ? $"{AvgFrequency.Value:F0} MHz" : "--";
    public string AvgPowerDisplay => AvgPower.HasValue ? $"{AvgPower.Value / 1000.0:F1} W" : "--";
    public string MaxTempDisplay => MaxTemp.HasValue ? $"{MaxTemp.Value:F0} ℃" : "--";
    public string EfficiencyDisplay => Efficiency.HasValue ? $"{Efficiency.Value:F0} Pts/W" : "--";
}
