using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace RyzenTunerNext.Service;

/// <summary>
/// 系统事件监听：电源切换、系统唤醒。
/// 收到事件时触发即时参数下发。
/// </summary>
public class SystemEventMonitor : IDisposable
{
    private readonly ILogger<SystemEventMonitor> _logger;

    /// <summary>系统唤醒时触发</summary>
    public event EventHandler? WakeUp;

    /// <summary>电源方案变更（AC/DC 切换）时触发</summary>
    public event EventHandler? PowerSourceChanged;

    public SystemEventMonitor(ILogger<SystemEventMonitor> logger)
    {
        _logger = logger;

        SystemEvents.PowerModeChanged += OnPowerModeChanged;
    }

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        switch (e.Mode)
        {
            case PowerModes.Resume:
                _logger.LogInformation("系统唤醒，触发参数下发");
                WakeUp?.Invoke(this, EventArgs.Empty);
                break;

            case PowerModes.StatusChange:
                _logger.LogInformation("电源状态变更，触发参数下发");
                PowerSourceChanged?.Invoke(this, EventArgs.Empty);
                break;
        }
    }

    public void Dispose()
    {
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
    }
}
