namespace RyzenTunerNext.Core.Models;

/// <summary>
/// CPU 实时指标
/// </summary>
public class CpuMetrics
{
    /// <summary>CPU 平均频率 (MHz)</summary>
    public float AvgFrequency { get; init; }

    /// <summary>整机封装功耗 (W)</summary>
    public float SocketPower { get; init; }

    /// <summary>CPU 温度 (°C)</summary>
    public float CpuTemp { get; init; }

    /// <summary>CPU 负载百分比</summary>
    public int CpuLoadPercent { get; init; }
}
