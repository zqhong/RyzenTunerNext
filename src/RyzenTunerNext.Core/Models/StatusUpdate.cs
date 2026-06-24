namespace RyzenTunerNext.Core.Models;

/// <summary>
/// 状态推送消息（Service → GUI）
/// </summary>
public class StatusUpdate
{
    public string Type => "StatusUpdate";
    public string Mode { get; init; } = string.Empty;
    public SetLimits? SetLimits { get; init; }
    public ActualLimitsData? ActualLimits { get; init; }
    public MetricsData? Metrics { get; init; }
}

public class SetLimits
{
    public double FastLimitW { get; init; }
    public double SlowLimitW { get; init; }
    public double TctlTempC { get; init; }
}

public class ActualLimitsData
{
    public double FastLimitW { get; init; }
    public double SlowLimitW { get; init; }
    public double TctlTempC { get; init; }
}

public class MetricsData
{
    public double CpuFreqMhz { get; init; }
    public double SocketPowerW { get; init; }
    public double CpuTempC { get; init; }
}
