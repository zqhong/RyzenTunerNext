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
}
