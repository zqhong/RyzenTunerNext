namespace RyzenTunerNext.Core.Models;

/// <summary>
/// Service 状态信息
/// </summary>
public class ServiceState
{
    public bool IsRunning { get; init; }
    public string? EngineVersion { get; init; }
    public string? CpuFamily { get; init; }
}
