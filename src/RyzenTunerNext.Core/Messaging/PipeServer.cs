using System.IO.Pipes;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace RyzenTunerNext.Core.Messaging;

/// <summary>
/// Named Pipe 服务端（Service 侧）。
/// 支持 GUI 断连后自动等待重连。
/// </summary>
public class PipeServer : IDisposable
{
    private readonly ILogger<PipeServer> _logger;
    private CancellationTokenSource? _cts;
    private Task? _listenTask;

    /// <summary>收到 GUI 消息时触发</summary>
    public event EventHandler<PipeMessage>? MessageReceived;

    /// <summary>GUI 连接状态变化时触发</summary>
    public event EventHandler<bool>? ClientConnected;

    public PipeServer(ILogger<PipeServer> logger)
    {
        _logger = logger;
    }

    public void Start(CancellationToken ct)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _listenTask = ListenLoopAsync(_cts.Token);
    }

    public async Task StopAsync()
    {
        _cts?.Cancel();
        if (_listenTask != null)
            await _listenTask;
    }

    private async Task ListenLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            NamedPipeServerStream? pipe = null;
            try
            {
                pipe = new NamedPipeServerStream(
                    PipeProtocol.PipeName,
                    PipeDirection.InOut,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                _logger.LogInformation("等待 GUI 连接...");
                await pipe.WaitForConnectionAsync(ct);
                _logger.LogInformation("GUI 已连接");
                ClientConnected?.Invoke(this, true);

                await HandleClientAsync(pipe, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Pipe 通信异常");
            }
            finally
            {
                ClientConnected?.Invoke(this, false);
                pipe?.Dispose();
            }
        }
    }

    private async Task HandleClientAsync(NamedPipeServerStream pipe, CancellationToken ct)
    {
        while (pipe.IsConnected && !ct.IsCancellationRequested)
        {
            try
            {
                var message = await PipeProtocol.ReadAsync(pipe, ct);
                if (message == null)
                {
                    _logger.LogInformation("GUI 断开连接");
                    break;
                }

                MessageReceived?.Invoke(this, message);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "读取消息失败");
                break;
            }
        }
    }

    /// <summary>
    /// 向已连接的 GUI 广播消息
    /// </summary>
    public async Task BroadcastAsync(PipeMessage message, CancellationToken ct = default)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", PipeProtocol.PipeName, PipeDirection.InOut);
            await pipe.ConnectAsync(100, ct);
            await PipeProtocol.SendAsync(pipe, message, ct);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "广播消息失败（GUI 可能未连接）");
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
    }
}
