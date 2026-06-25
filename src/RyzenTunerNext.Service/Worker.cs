using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RyzenTunerNext.Core.Data;
using RyzenTunerNext.Core.Messaging;
using RyzenTunerNext.Core.Models;
using RyzenTunerNext.Core.Services;

namespace RyzenTunerNext.Service;

/// <summary>
/// BackgroundService 主循环：协调各模块完成参数下发。
/// </summary>
public class Worker : BackgroundService
{
    private readonly RyzenAdjWrapper _ryzenAdj;
    private readonly SettingsRepository _settings;
    private readonly LogRepository _logs;
    private readonly StatusCacheRepository _statusCache;
    private readonly PipeServer _pipeServer;
    private readonly SystemEventMonitor _eventMonitor;
    private readonly ModeScheduler _modeScheduler;
    private readonly ParameterApplier _parameterApplier;
    private readonly ILogger<Worker> _logger;

    private bool _immediateApply;

    public Worker(
        RyzenAdjWrapper ryzenAdj,
        SettingsRepository settings,
        LogRepository logs,
        StatusCacheRepository statusCache,
        PipeServer pipeServer,
        SystemEventMonitor eventMonitor,
        ModeScheduler modeScheduler,
        ParameterApplier parameterApplier,
        ILogger<Worker> logger)
    {
        _ryzenAdj = ryzenAdj;
        _settings = settings;
        _logs = logs;
        _statusCache = statusCache;
        _pipeServer = pipeServer;
        _eventMonitor = eventMonitor;
        _modeScheduler = modeScheduler;
        _parameterApplier = parameterApplier;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 1. 启动 Named Pipe Server（尽早启动，让 GUI 可以立即连接）
        _pipeServer.MessageReceived += OnGuiMessage;
        _pipeServer.Start(stoppingToken);

        // 2. 注册系统事件
        _eventMonitor.WakeUp += OnWakeUp;
        _eventMonitor.PowerSourceChanged += OnPowerSourceChanged;

        // 3. 发送 Service 状态（GUI 连接后立即可见）
        await BroadcastServiceStateAsync(stoppingToken);

        // 4. 初始化 RyzenAdj（可能耗时较长，但 PipeServer 已就绪，GUI 不会卡在"未连接"）
        await InitializeRyzenAdjAsync(stoppingToken);

        // 5. 启动日志清理后台任务
        _ = RunLogCleanupLoopAsync(stoppingToken);

        // 6. 主循环
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // 评估当前模式
                var profile = await _parameterApplier.GetCurrentProfileAsync();

                // 如果是 Auto 模式，由 ModeScheduler 评估
                var mode = await _settings.GetEnergyModeAsync();
                if (mode == "Auto")
                {
                    var evaluated = await _modeScheduler.EvaluateAsync();
                    if (evaluated == EnergyMode.PowerSaving)
                    {
                        profile = PowerProfile.PowerSaving(
                            await _settings.GetFastLimitPowersavingAsync(),
                            await _settings.GetSlowLimitPowersavingAsync(),
                            await _settings.GetTctlTempAsync());
                    }
                    else
                    {
                        profile = PowerProfile.Performance(
                            await _settings.GetFastLimitPerformanceAsync(),
                            await _settings.GetSlowLimitPerformanceAsync(),
                            await _settings.GetTctlTempAsync());
                    }
                }

                // 下发参数并验证
                var result = await _parameterApplier.ApplyAndVerifyAsync(profile);

                // 推送状态到 GUI
                await BroadcastStatusAsync(profile, result, stoppingToken);

                // 等待间隔，但每 100ms 检查一次 immediateApply
                var interval = await _settings.GetApplyIntervalAsync();
                var elapsed = 0;
                _immediateApply = false;

                while (elapsed < interval && !stoppingToken.IsCancellationRequested)
                {
                    if (_immediateApply)
                    {
                        _logger.LogInformation("收到立即应用信号，跳过等待");
                        _immediateApply = false;
                        break;
                    }
                    await Task.Delay(100, stoppingToken);
                    elapsed += 100;
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "主循环异常");
                await _logs.ErrorAsync("Service", $"参数下发异常: {ex.Message}");
                await Task.Delay(5000, stoppingToken);
            }
        }
    }

    private async Task InitializeRyzenAdjAsync(CancellationToken ct)
    {
        const int maxRetries = 10;
        for (int i = 0; i < maxRetries; i++)
        {
            if (_ryzenAdj.Initialize())
            {
                _logger.LogInformation("RyzenAdj 初始化成功，CPU Family: {Family}", _ryzenAdj.GetCpuFamily());
                await _logs.InfoAsync("Service", $"RyzenAdj 初始化成功，CPU Family: {_ryzenAdj.GetCpuFamily()}");
                return;
            }

            _logger.LogError("RyzenAdj 初始化失败，30 秒后重试 ({Attempt}/{Max})", i + 1, maxRetries);
            await _logs.ErrorAsync("Service", $"RyzenAdj 初始化失败，30 秒后重试 ({i + 1}/{maxRetries})");
            await Task.Delay(30000, ct);
        }

        _logger.LogCritical("RyzenAdj 初始化失败，已达最大重试次数");
        await _logs.ErrorAsync("Service", "RyzenAdj 初始化失败，已达最大重试次数");
    }

    private void OnWakeUp(object? sender, EventArgs e)
    {
        _immediateApply = true;
    }

    private void OnPowerSourceChanged(object? sender, EventArgs e)
    {
        // AC/DC 切换时：立即下发参数 + 重评估自动模式状态
        _modeScheduler.Reset();
        _immediateApply = true;
    }

    private void OnGuiMessage(object? sender, PipeMessage message)
    {
        switch (message)
        {
            case SetModeMessage setMode:
                _ = _settings.SetAsync("energy_mode", setMode.Mode);
                _modeScheduler.Reset();
                _logger.LogInformation("模式切换: {Mode}", setMode.Mode);
                break;

            case ApplyNowMessage:
                _immediateApply = true;
                break;

            case UpdateConfigMessage update:
                _ = _settings.SetAsync(update.Key, update.Value);
                _logger.LogInformation("配置更新: {Key} = {Value}", update.Key, update.Value);
                break;

            case RequestStatusMessage:
                _immediateApply = true;
                break;
        }
    }

    private async Task BroadcastStatusAsync(PowerProfile profile, ApplyResult result, CancellationToken ct)
    {
        if (result.Actual == null) return;

        var metrics = _ryzenAdj.ReadCpuMetrics();

        var message = new StatusUpdateMessage
        {
            Mode = profile.Mode.ToString(),
            SetLimits = new LimitData
            {
                FastLimit = profile.FastLimit,
                SlowLimit = profile.SlowLimit,
                TctlTemp = profile.TctlTemp
            },
            ActualLimits = new ActualLimitData
            {
                FastLimit = result.Actual.FastLimit,
                SlowLimit = result.Actual.SlowLimit,
                TctlTemp = result.Actual.TctlTemp,
                SocketPower = result.Actual.SocketPower,
                CpuTemp = metrics.CpuTemp,
                CpuFrequency = metrics.AvgFrequency
            }
        };

        // 通过 Named Pipe 推送给 GUI
        await _pipeServer.BroadcastAsync(message, ct);

        // 更新 SQLite 状态缓存（GUI 重启后可恢复状态显示）
        try
        {
            var json = JsonSerializer.Serialize(message);
            await _statusCache.SetAsync("last_status", json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "写入状态缓存失败");
        }
    }

    private async Task BroadcastServiceStateAsync(CancellationToken ct)
    {
        var message = new ServiceStateMessage
        {
            IsRunning = true,
            EngineVersion = "0.19.0",
            CpuFamily = _ryzenAdj.IsInitialized ? _ryzenAdj.GetCpuFamily().ToString() : "Unknown"
        };

        await _pipeServer.BroadcastAsync(message, ct);
    }

    /// <summary>
    /// 日志自动清理：每 6 小时执行一次，按配置的保留天数清理过期日志。
    /// </summary>
    private async Task RunLogCleanupLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var retentionDays = await _settings.GetLogRetentionDaysAsync();
                await _logs.CleanupOlderThanAsync(retentionDays);
                _logger.LogDebug("日志清理完成，保留 {Days} 天", retentionDays);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "日志清理失败");
            }

            // 每 6 小时清理一次
            await Task.Delay(TimeSpan.FromHours(6), ct);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Service 正在停止...");
        await _pipeServer.StopAsync();
        _ryzenAdj.Dispose();
        _eventMonitor.Dispose();
        await base.StopAsync(cancellationToken);
    }
}
