using System.Buffers.Binary;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace RyzenTunerNext.Core.Messaging;

/// <summary>
/// Named Pipe 协议工具：长度前缀 (4字节 uint32 LE) + UTF-8 JSON
/// </summary>
public static class PipeProtocol
{
    public const string PipeName = "RyzenTunerNext_Pipe";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    /// <summary>
    /// 发送消息到流
    /// </summary>
    public static async Task SendAsync(Stream stream, PipeMessage message, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(message, JsonOptions);
        var payload = Encoding.UTF8.GetBytes(json);

        var lengthBuffer = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(lengthBuffer, (uint)payload.Length);

        await stream.WriteAsync(lengthBuffer, ct);
        await stream.WriteAsync(payload, ct);
        await stream.FlushAsync(ct);
    }

    /// <summary>
    /// 从流读取消息
    /// </summary>
    public static async Task<PipeMessage?> ReadAsync(Stream stream, CancellationToken ct = default)
    {
        var lengthBuffer = new byte[4];
        if (!await ReadExactlyAsync(stream, lengthBuffer, 4, ct))
            return null;

        var length = BinaryPrimitives.ReadUInt32LittleEndian(lengthBuffer);
        if (length > 1024 * 1024) // 限制 1MB
            throw new ProtocolViolationException("消息长度超过 1MB 限制");

        var payloadBuffer = new byte[length];
        if (!await ReadExactlyAsync(stream, payloadBuffer, (int)length, ct))
            return null;

        var json = Encoding.UTF8.GetString(payloadBuffer);
        return JsonSerializer.Deserialize<PipeMessage>(json, JsonOptions);
    }

    private static async Task<bool> ReadExactlyAsync(Stream buffer, byte[] destination, int count, CancellationToken ct)
    {
        int totalRead = 0;
        while (totalRead < count)
        {
            int read = await buffer.ReadAsync(destination.AsMemory(totalRead, count - totalRead), ct);
            if (read == 0) return false;
            totalRead += read;
        }
        return true;
    }
}
