using System.Text.Json;
using Microsoft.Extensions.Logging;
using RyzenTunerNext.Core.Data;
using RyzenTunerNext.Core.Messaging;
using RyzenTunerNext.Core.Models;
using RyzenTunerNext.Core.Services;

namespace RyzenTunerNext.App.Services;

/// <summary>
/// 后台功耗管理器。
/// 替代原 Service 进程中的 Worker + PipeServer，在 App 进程内直接运行。
/// </summary>
public class PowerManager
{
    private readonly RyzenAdjWrapper _ryzenAdj;
    private readonly SettingsRepository _settings;
    private readonly LogRepository _logs;
    private readonly StatusCacheRepository _statusCache;
    private readonly ParameterApplier _parameterApplier;
    private readonly ModeScheduler _modeScheduler;
    private readonly SystemEventMonitor _eventMonitor;
    private readonly ILogger<PowerManager> _logger;

    private bool _immediateApply;
    private CancellationTokenSource? _cts;
    private Task? _mainLoopTask;

    /// <summary>状态更新事件（替代 PipeClient 的 StatusUpdateMessage）</summary>
    public event EventHandler<StatusUpdateMessage>? StatusUpdated;

    /// <summary>Service 状态变更事件（替代 PipeClient 的 ServiceStateMessage）</summary>
    public event EventHandler<ServiceStateMessage>? StateChanged;

    public PowerManager(
        RyzenAdjWrapper ryzenAdj,
        SettingsRepository settings,
        LogRepository logs,
        StatusCacheRepository statusCache,
        ParameterApplier parameterApplier,
        ModeScheduler modeScheduler,
        SystemEventMonitor eventMonitor,
        ILogger<PowerManager> logger)
    {
        _ryzenAdj = ryzenAdj;
        _settings = settings;
        _logs = logs;
        _statusCache = statusCache;
        _parameterApplier = parameterApplier;
        _modeScheduler = modeScheduler;
        _eventMonitor = eventMonitor;
        _logger = logger;
    }

    /// <summary>
    /// 启动后台功耗管理循环。
    /// </summary>
    public async Task StartAsync(CancellationToken ct)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        // 1. 注册系统事件
        _eventMonitor.WakeUp += OnWakeUp;
        _eventMonitor.PowerSourceChanged += OnPowerSourceChanged;

        // 2. 发送初始状态
        BroadcastServiceState();

        // 3. 初始化 RyzenAdj（可能耗时较长）
        await InitializeRyzenAdjAsync(_cts.Token);

        // 4. 启动日志清理后台任务
        _ = RunLogCleanupLoopAsync(_cts.Token);

        // 5. 启动主循环
        _mainLoopTask = Task.Run(() => MainLoopAsync(_cts.Token), _cts.Token);
    }

    /// <summary>
    /// 停止后台循环并清理资源。
    /// </summary>
    public async Task StopAsync()
    {
        _logger.LogInformation("PowerManager 正在停止...");

        _cts?.Cancel();

        if (_mainLoopTask != null)
        {
            try
            {
                await _mainLoopTask;
            }
            catch (OperationCanceledException)
            {
                // 正常取消
            }
        }

        _eventMonitor.WakeUp -= OnWakeUp;
        _eventMonitor.PowerSourceChanged -= OnPowerSourceChanged;
        _eventMonitor.Dispose();
        _ryzenAdj.Dispose();
    }

    #region 公开方法（替代 PipeClient.SendAsync）

    /// <summary>
    /// 切换能耗模式。
    /// </summary>
    public void SetMode(string mode)
    {
        _ = _settings.SetAsync("energy_mode", mode);
        _modeScheduler.Reset();
        _immediateApply = true;
        _logger.LogInformation("模式切换: {Mode}", mode);
    }

    /// <summary>
    /// 立即应用当前参数。
    /// </summary>
    public void ApplyNow()
    {
        _immediateApply = true;
    }

    /// <summary>
    /// 更新配置项。
    /// </summary>
    public void UpdateConfig(string key, string value)
    {
        _ = _settings.SetAsync(key, value);
        _immediateApply = true;
        _logger.LogInformation("配置更新: {Key} = {Value}", key, value);
    }

    #endregion

    #region 主循环

    private async Task MainLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
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

                // 推送状态
                await BroadcastStatusAsync(profile, result);

                // 等待间隔，但每 100ms 检查一次 immediateApply
                var interval = await _settings.GetApplyIntervalAsync();
                var elapsed = 0;
                _immediateApply = false;

                while (elapsed < interval && !ct.IsCancellationRequested)
                {
                    if (_immediateApply)
                    {
                        _logger.LogInformation("收到立即应用信号，跳过等待");
                        _immediateApply = false;
                        break;
                    }
                    await Task.Delay(100, ct);
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
                await Task.Delay(5000, ct);
            }
        }
    }

    #endregion

    #region 初始化

    private async Task InitializeRyzenAdjAsync(CancellationToken ct)
    {
        const int maxRetries = 10;
        for (int i = 0; i < maxRetries; i++)
        {
            var (success, error) = _ryzenAdj.Initialize();
            if (success)
            {
                _logger.LogInformation("RyzenAdj 初始化成功，CPU Family: {Family}", _ryzenAdj.GetCpuFamily());
                await _logs.InfoAsync("Service", $"RyzenAdj 初始化成功，CPU Family: {_ryzenAdj.GetCpuFamily()}");
                return;
            }

            _logger.LogError("RyzenAdj 初始化失败: {Error}，30 秒后重试 ({Attempt}/{Max})", error, i + 1, maxRetries);
            await _logs.ErrorAsync("Service", $"RyzenAdj 初始化失败: {error}，30 秒后重试 ({i + 1}/{maxRetries})");
            await Task.Delay(30000, ct);
        }

        _logger.LogCritical("RyzenAdj 初始化失败，已达最大重试次数");
        await _logs.ErrorAsync("Service", "RyzenAdj 初始化失败，已达最大重试次数");
    }

    #endregion

    #region 事件处理

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

    #endregion

    #region 状态广播

    private async Task BroadcastStatusAsync(PowerProfile profile, ApplyResult result)
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

        // 触发事件通知 UI
        StatusUpdated?.Invoke(this, message);

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

    private void BroadcastServiceState()
    {
        var message = new ServiceStateMessage
        {
            IsRunning = true,
            EngineVersion = "0.19.0",
            CpuFamily = _ryzenAdj.IsInitialized ? _ryzenAdj.GetCpuFamily().ToString() : "Unknown"
        };

        StateChanged?.Invoke(this, message);
    }

    #endregion

    #region 日志清理

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

    #endregion
}
