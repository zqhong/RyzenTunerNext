using System.IO.Pipes;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace RyzenTunerNext.Core.Messaging;

/// <summary>
/// Named Pipe 服务端（Service 侧）。
/// 支持 GUI 断连后自动等待重连。
/// 通过已连接的 pipe stream 双向通信（读取 GUI 消息 + 广播状态到 GUI）。
/// </summary>
public class PipeServer : IDisposable
{
    private readonly ILogger<PipeServer> _logger;
    private CancellationTokenSource? _cts;
    private Task? _listenTask;

    // 已连接的 GUI pipe stream（读写共享，通过 _writeLock 保护写操作）
    private NamedPipeServerStream? _connectedPipe;
    private readonly object _writeLock = new();
    private bool _clientConnected;

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

                lock (_writeLock)
                {
                    _connectedPipe = pipe;
                    _clientConnected = true;
                }
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
                lock (_writeLock)
                {
                    _connectedPipe = null;
                    _clientConnected = false;
                }
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
    /// 通过已连接的 pipe stream 向 GUI 广播消息。
    /// 使用锁保护写操作，避免与并发写入冲突。
    /// </summary>
    public async Task BroadcastAsync(PipeMessage message, CancellationToken ct = default)
    {
        NamedPipeServerStream? pipe;
        lock (_writeLock)
        {
            if (!_clientConnected || _connectedPipe == null || !_connectedPipe.IsConnected)
            {
                _logger.LogDebug("GUI 未连接，跳过广播");
                return;
            }
            pipe = _connectedPipe;
        }

        try
        {
            // 写操作需要同步，避免多线程并发写入导致消息交错
            lock (_writeLock)
            {
                if (!_clientConnected || _connectedPipe == null || !_connectedPipe.IsConnected)
                    return;
                PipeProtocol.SendAsync(pipe, message, ct).GetAwaiter().GetResult();
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "广播消息失败（GUI 可能已断开）");
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        lock (_writeLock)
        {
            _connectedPipe?.Dispose();
            _connectedPipe = null;
        }
        _cts?.Dispose();
    }
}
