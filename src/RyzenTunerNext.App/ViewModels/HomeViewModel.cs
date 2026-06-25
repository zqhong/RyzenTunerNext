using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RyzenTunerNext.Core.Messaging;

namespace RyzenTunerNext.App.ViewModels;

public partial class HomeViewModel : ObservableObject
{
    [ObservableProperty] public partial bool IsConnected { get; set; }
    [ObservableProperty] public partial string CurrentMode { get; set; } = "Auto";
    [ObservableProperty] public partial double CpuFrequency { get; set; }
    [ObservableProperty] public partial double SocketPower { get; set; }
    [ObservableProperty] public partial double CpuTemp { get; set; }

    [ObservableProperty] public partial double FastLimitSet { get; set; }
    [ObservableProperty] public partial double SlowLimitSet { get; set; }
    [ObservableProperty] public partial double TctlTempSet { get; set; }

    [ObservableProperty] public partial double FastLimitActual { get; set; }
    [ObservableProperty] public partial double SlowLimitActual { get; set; }
    [ObservableProperty] public partial double TctlTempActual { get; set; }

    [ObservableProperty] public partial string? CapWarning { get; set; }

    // 格式化显示属性（WinUI 3 的 Binding 不支持 StringFormat）
    public string CpuFrequencyDisplay => CpuFrequency.ToString("F0");
    public string SocketPowerDisplay => SocketPower.ToString("F1");
    public string CpuTempDisplay => CpuTemp.ToString("F0");
    public string FastLimitActualDisplay => FastLimitActual.ToString("F1");
    public string SlowLimitActualDisplay => SlowLimitActual.ToString("F1");
    public string TctlTempActualDisplay => TctlTempActual.ToString("F0");

    // 属性变更时通知格式化属性更新
    partial void OnCpuFrequencyChanged(double value) => OnPropertyChanged(nameof(CpuFrequencyDisplay));
    partial void OnSocketPowerChanged(double value) => OnPropertyChanged(nameof(SocketPowerDisplay));
    partial void OnCpuTempChanged(double value) => OnPropertyChanged(nameof(CpuTempDisplay));
    partial void OnFastLimitActualChanged(double value) => OnPropertyChanged(nameof(FastLimitActualDisplay));
    partial void OnSlowLimitActualChanged(double value) => OnPropertyChanged(nameof(SlowLimitActualDisplay));
    partial void OnTctlTempActualChanged(double value) => OnPropertyChanged(nameof(TctlTempActualDisplay));

    [RelayCommand]
    private async Task SwitchModeAsync(string mode)
    {
        CurrentMode = mode;
        App.PowerManager.SetMode(mode);
        await App.Settings.SetAsync("energy_mode", mode);
    }

    [RelayCommand]
    private Task ApplyNowAsync()
    {
        App.PowerManager.ApplyNow();
        return Task.CompletedTask;
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
