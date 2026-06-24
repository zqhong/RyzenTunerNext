namespace RyzenTunerNext.Core.Models;

/// <summary>
/// 运行日志条目
/// </summary>
public class LogEntry
{
    public long Id { get; set; }
    public string Timestamp { get; set; } = string.Empty;
    public string Level { get; set; } = "Info";
    public string Source { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? Detail { get; set; }
}
