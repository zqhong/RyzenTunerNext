using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RyzenTunerNext.Core.Messaging;

namespace RyzenTunerNext.App.ViewModels;

public partial class HomeViewModel : ObservableObject
{
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private string _currentMode = "Auto";
    [ObservableProperty] private double _cpuFrequency;
    [ObservableProperty] private double _socketPower;
    [ObservableProperty] private double _cpuTemp;

    [ObservableProperty] private double _fastLimitSet;
    [ObservableProperty] private double _slowLimitSet;
    [ObservableProperty] private double _tctlTempSet;

    [ObservableProperty] private double _fastLimitActual;
    [ObservableProperty] private double _slowLimitActual;
    [ObservableProperty] private double _tctlTempActual;

    [ObservableProperty] private string? _capWarning;

    [RelayCommand]
    private async Task SwitchModeAsync(string mode)
    {
        CurrentMode = mode;
        await App.PipeClient.SendAsync(new SetModeMessage { Mode = mode });
        await App.Settings.SetAsync("energy_mode", mode);
    }

    [RelayCommand]
    private async Task ApplyNowAsync()
    {
        await App.PipeClient.SendAsync(new ApplyNowMessage());
    }

    public void UpdateFromStatus(StatusUpdateMessage msg)
    {
        CurrentMode = msg.Mode;

        if (msg.SetLimits != null)
        {
            FastLimitSet = msg.SetLimits.FastLimit / 1000.0;
            SlowLimitSet = msg.SetLimits.SlowLimit / 1000.0;
            TctlTempSet = msg.SetLimits.TctlTemp;
        }

        if (msg.ActualLimits != null)
        {
            FastLimitActual = msg.ActualLimits.FastLimit / 1000.0;
            SlowLimitActual = msg.ActualLimits.SlowLimit / 1000.0;
            TctlTempActual = msg.ActualLimits.TctlTemp;
            SocketPower = msg.ActualLimits.SocketPower / 1000.0;
            CpuTemp = msg.ActualLimits.CpuTemp;
            CpuFrequency = msg.ActualLimits.CpuFrequency;
        }

        // 检查 cap 警告
        UpdateCapWarning();
    }

    private void UpdateCapWarning()
    {
        var warnings = new List<string>();

        if (Math.Abs(FastLimitSet - FastLimitActual) > 1.0)
            warnings.Add($"Fast Limit 设置 {FastLimitSet:F0}W，实际生效 {FastLimitActual:F1}W（可能被 BIOS 锁定）");

        if (Math.Abs(SlowLimitSet - SlowLimitActual) > 1.0)
            warnings.Add($"Slow Limit 设置 {SlowLimitSet:F0}W，实际生效 {SlowLimitActual:F1}W（可能被 BIOS 锁定）");

        CapWarning = warnings.Count > 0 ? string.Join("\n", warnings) : null;
    }
}
