using System.Text.Json;
using System.Text.Json.Serialization;

namespace RyzenTunerNext.Core.Messaging;

/// <summary>
/// Named Pipe 消息基类
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(StatusUpdateMessage), "status_update")]
[JsonDerivedType(typeof(LogMessage), "log")]
[JsonDerivedType(typeof(ServiceStateMessage), "service_state")]
[JsonDerivedType(typeof(SetModeMessage), "set_mode")]
[JsonDerivedType(typeof(ApplyNowMessage), "apply_now")]
[JsonDerivedType(typeof(UpdateConfigMessage), "update_config")]
[JsonDerivedType(typeof(RequestStatusMessage), "request_status")]
public abstract class PipeMessage
{
    // "type" 字段由 System.Text.Json 多态序列化器自动处理

    [JsonPropertyName("timestamp")]
    public string Timestamp { get; set; } = DateTime.UtcNow.ToString("o");
}

// ===== Service → GUI =====

public class StatusUpdateMessage : PipeMessage
{
    [JsonPropertyName("mode")]
    public string Mode { get; set; } = string.Empty;

    [JsonPropertyName("set_limits")]
    public LimitData? SetLimits { get; set; }

    [JsonPropertyName("actual_limits")]
    public ActualLimitData? ActualLimits { get; set; }
}

public class LimitData
{
    [JsonPropertyName("fast_limit")]
    public int FastLimit { get; set; }

    [JsonPropertyName("slow_limit")]
    public int SlowLimit { get; set; }

    [JsonPropertyName("tctl_temp")]
    public int TctlTemp { get; set; }
}

public class ActualLimitData
{
    [JsonPropertyName("fast_limit")]
    public float FastLimit { get; set; }

    [JsonPropertyName("slow_limit")]
    public float SlowLimit { get; set; }

    [JsonPropertyName("tctl_temp")]
    public float TctlTemp { get; set; }

    [JsonPropertyName("socket_power")]
    public float SocketPower { get; set; }

    [JsonPropertyName("cpu_temp")]
    public float CpuTemp { get; set; }
}

public class LogMessage : PipeMessage
{
    [JsonPropertyName("level")]
    public string Level { get; set; } = "Info";

    [JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty;

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
}

public class ServiceStateMessage : PipeMessage
{
    [JsonPropertyName("is_running")]
    public bool IsRunning { get; set; }

    [JsonPropertyName("engine_version")]
    public string? EngineVersion { get; set; }

    [JsonPropertyName("cpu_family")]
    public string? CpuFamily { get; set; }
}

// ===== GUI → Service =====

public class SetModeMessage : PipeMessage
{
    [JsonPropertyName("mode")]
    public string Mode { get; set; } = string.Empty;
}

public class ApplyNowMessage : PipeMessage
{
}

public class UpdateConfigMessage : PipeMessage
{
    [JsonPropertyName("key")]
    public string Key { get; set; } = string.Empty;

    [JsonPropertyName("value")]
    public string Value { get; set; } = string.Empty;
}

public class RequestStatusMessage : PipeMessage
{
}
