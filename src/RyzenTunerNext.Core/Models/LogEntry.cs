namespace RyzenTunerNext.Core.Models;

/// <summary>
/// 运行日志条目
/// </summary>
public class LogEntry
{
    public long Id { get; init; }
    public string Timestamp { get; init; } = string.Empty;
    public string Level { get; init; } = "Info";
    public string Source { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string? Detail { get; init; }
}
