using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RyzenTunerNext.Core.Messaging;

namespace RyzenTunerNext.App.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    [ObservableProperty] private int _fastLimitPerformance = 45000;
    [ObservableProperty] private int _slowLimitPerformance = 45000;
    [ObservableProperty] private int _fastLimitPowersaving = 25000;
    [ObservableProperty] private int _slowLimitPowersaving = 15000;
    [ObservableProperty] private int _tctlTemp = 90;
    [ObservableProperty] private int _applyInterval = 4000;
    [ObservableProperty] private int _logRetentionDays = 30;

    public async Task LoadAsync()
    {
        FastLimitPerformance = await App.Settings.GetFastLimitPerformanceAsync();
        SlowLimitPerformance = await App.Settings.GetSlowLimitPerformanceAsync();
        FastLimitPowersaving = await App.Settings.GetFastLimitPowersavingAsync();
        SlowLimitPowersaving = await App.Settings.GetSlowLimitPowersavingAsync();
        TctlTemp = await App.Settings.GetTctlTempAsync();
        ApplyInterval = await App.Settings.GetApplyIntervalAsync();
        LogRetentionDays = await App.Settings.GetLogRetentionDaysAsync();
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        await SaveSettingAsync("fast_limit_performance", FastLimitPerformance.ToString());
        await SaveSettingAsync("slow_limit_performance", SlowLimitPerformance.ToString());
        await SaveSettingAsync("fast_limit_powersaving", FastLimitPowersaving.ToString());
        await SaveSettingAsync("slow_limit_powersaving", SlowLimitPowersaving.ToString());
        await SaveSettingAsync("tctl_temp", TctlTemp.ToString());
        await SaveSettingAsync("apply_interval", ApplyInterval.ToString());
        await SaveSettingAsync("log_retention_days", LogRetentionDays.ToString());
    }

    private async Task SaveSettingAsync(string key, string value)
    {
        await App.Settings.SetAsync(key, value);
        await App.PipeClient.SendAsync(new UpdateConfigMessage { Key = key, Value = value });
    }
}
