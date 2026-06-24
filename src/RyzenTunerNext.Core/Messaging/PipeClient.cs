using System.IO.Pipes;
using Microsoft.Extensions.Logging;

namespace RyzenTunerNext.Core.Messaging;

/// <summary>
/// Named Pipe 客户端（GUI 侧）。
/// 自动重连（指数退避）。
/// </summary>
public class PipeClient : IDisposable
{
    private readonly ILogger<PipeClient> _logger;
    private CancellationTokenSource? _cts;
    private Task? _listenTask;
    private NamedPipeClientStream? _pipe;

    /// <summary>收到 Service 消息时触发</summary>
    public event EventHandler<PipeMessage>? MessageReceived;

    /// <summary>连接状态变化时触发</summary>
    public event EventHandler<bool>? ConnectionChanged;

    public bool IsConnected => _pipe?.IsConnected == true;

    public PipeClient(ILogger<PipeClient> logger)
    {
        _logger = logger;
    }

    public void Start(CancellationToken ct)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _listenTask = ConnectAndListenAsync(_cts.Token);
    }

    public async Task StopAsync()
    {
        _cts?.Cancel();
        if (_listenTask != null)
            await _listenTask;
    }

    private async Task ConnectAndListenAsync(CancellationToken ct)
    {
        var retryDelay = 1000; // 起始 1 秒
        const int maxDelay = 30000; // 最大 30 秒

        while (!ct.IsCancellationRequested)
        {
            try
            {
                _pipe?.Dispose();
                _pipe = new NamedPipeClientStream(".", PipeProtocol.PipeName, PipeDirection.InOut);

                _logger.LogInformation("正在连接 Service...");
                await _pipe.ConnectAsync(5000, ct);
                _logger.LogInformation("已连接到 Service");
                ConnectionChanged?.Invoke(this, true);

                retryDelay = 1000; // 重置重连延迟

                await ListenMessagesAsync(_pipe, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (TimeoutException)
            {
                _logger.LogDebug("连接超时，重试中...");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "连接异常");
            }
            finally
            {
                ConnectionChanged?.Invoke(this, false);
            }

            if (!ct.IsCancellationRequested)
            {
                await Task.Delay(retryDelay, ct);
                retryDelay = Math.Min(retryDelay * 2, maxDelay);
            }
        }
    }

    private async Task ListenMessagesAsync(NamedPipeClientStream pipe, CancellationToken ct)
    {
        while (pipe.IsConnected && !ct.IsCancellationRequested)
        {
            try
            {
                var message = await PipeProtocol.ReadAsync(pipe, ct);
                if (message == null)
                {
                    _logger.LogInformation("Service 断开连接");
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
    /// 发送消息到 Service
    /// </summary>
    public async Task SendAsync(PipeMessage message, CancellationToken ct = default)
    {
        if (_pipe == null || !_pipe.IsConnected)
        {
            _logger.LogWarning("未连接到 Service，无法发送消息");
            return;
        }

        try
        {
            await PipeProtocol.SendAsync(_pipe, message, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "发送消息失败");
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _pipe?.Dispose();
        _cts?.Dispose();
    }
}
