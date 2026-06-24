using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using RyzenTunerNext.App.Helpers;
using RyzenTunerNext.Core.Messaging;

namespace RyzenTunerNext.App.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    // ===== 功耗参数 =====
    [ObservableProperty] private int _fastLimitPerformance = 45000;
    [ObservableProperty] private int _slowLimitPerformance = 45000;
    [ObservableProperty] private int _fastLimitPowersaving = 25000;
    [ObservableProperty] private int _slowLimitPowersaving = 15000;
    [ObservableProperty] private int _tctlTemp = 90;
    [ObservableProperty] private int _applyInterval = 4000;
    [ObservableProperty] private int _logRetentionDays = 30;

    // ===== Service 管理 =====
    [ObservableProperty] private bool _serviceInstalled;
    [ObservableProperty] private bool _serviceRunning;
    [ObservableProperty] private string _serviceStatusText = "检测中...";

    // ===== 主题和语言 =====
    [ObservableProperty] private int _selectedThemeIndex;  // 0=跟随系统, 1=亮色, 2=暗色
    [ObservableProperty] private int _selectedLanguageIndex;  // 0=跟随系统, 1=中文, 2=English

    public string[] ThemeOptions { get; } = ["跟随系统", "亮色", "暗色"];
    public string[] LanguageOptions { get; } = ["跟随系统", "中文", "English"];

    // ===== 快捷键 =====
    [ObservableProperty] private string _hotkeyToggleMode = "";
    [ObservableProperty] private string _hotkeyApplyNow = "";
    [ObservableProperty] private string _hotkeyShowWindow = "";

    // ===== 加载/保存 =====

    public async Task LoadAsync()
    {
        // 功耗参数
        FastLimitPerformance = await App.Settings.GetFastLimitPerformanceAsync();
        SlowLimitPerformance = await App.Settings.GetSlowLimitPerformanceAsync();
        FastLimitPowersaving = await App.Settings.GetFastLimitPowersavingAsync();
        SlowLimitPowersaving = await App.Settings.GetSlowLimitPowersavingAsync();
        TctlTemp = await App.Settings.GetTctlTempAsync();
        ApplyInterval = await App.Settings.GetApplyIntervalAsync();
        LogRetentionDays = await App.Settings.GetLogRetentionDaysAsync();

        // Service 状态
        RefreshServiceStatus();

        // 主题和语言
        var theme = await App.Settings.GetAsync("theme") ?? "system";
        SelectedThemeIndex = theme switch { "light" => 1, "dark" => 2, _ => 0 };
        var lang = await App.Settings.GetAsync("language") ?? "system";
        SelectedLanguageIndex = lang switch { "zh" => 1, "en" => 2, _ => 0 };

        // 快捷键
        HotkeyToggleMode = await App.Settings.GetAsync("hotkey_toggle_mode") ?? "";
        HotkeyApplyNow = await App.Settings.GetAsync("hotkey_apply_now") ?? "";
        HotkeyShowWindow = await App.Settings.GetAsync("hotkey_show_window") ?? "";
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

        // 主题和语言
        var theme = SelectedThemeIndex switch { 1 => "light", 2 => "dark", _ => "system" };
        await App.Settings.SetAsync("theme", theme);
        ApplyTheme(theme);

        var lang = SelectedLanguageIndex switch { 1 => "zh", 2 => "en", _ => "system" };
        await App.Settings.SetAsync("language", lang);

        // 快捷键
        await App.Settings.SetAsync("hotkey_toggle_mode", HotkeyToggleMode);
        await App.Settings.SetAsync("hotkey_apply_now", HotkeyApplyNow);
        await App.Settings.SetAsync("hotkey_show_window", HotkeyShowWindow);
    }

    private async Task SaveSettingAsync(string key, string value)
    {
        await App.Settings.SetAsync(key, value);
        await App.PipeClient.SendAsync(new UpdateConfigMessage { Key = key, Value = value });
    }

    // ===== Service 管理命令 =====

    [RelayCommand]
    private void RefreshServiceStatus()
    {
        var state = ServiceManager.GetServiceState();
        ServiceInstalled = state.IsInstalled;
        ServiceRunning = state.IsRunning;
        ServiceStatusText = state.StatusText;
    }

    [RelayCommand]
    private async Task InstallServiceAsync()
    {
        var (success, message) = await ServiceManager.InstallAsync();
        RefreshServiceStatus();
        if (!success)
        {
            await ShowErrorDialogAsync(message);
        }
    }

    [RelayCommand]
    private async Task UninstallServiceAsync()
    {
        var (success, message) = await ServiceManager.UninstallAsync();
        RefreshServiceStatus();
        if (!success)
        {
            await ShowErrorDialogAsync(message);
        }
    }

    [RelayCommand]
    private async Task StartServiceAsync()
    {
        var (success, message) = await ServiceManager.StartAsync();
        RefreshServiceStatus();
        if (!success)
        {
            await ShowErrorDialogAsync(message);
        }
    }

    [RelayCommand]
    private async Task StopServiceAsync()
    {
        var (success, message) = await ServiceManager.StopAsync();
        RefreshServiceStatus();
        if (!success)
        {
            await ShowErrorDialogAsync(message);
        }
    }

    // ===== 主题切换 =====

    private static void ApplyTheme(string theme)
    {
        if (App.MainWindow?.Content is FrameworkElement root)
        {
            root.RequestedTheme = theme switch
            {
                "light" => ElementTheme.Light,
                "dark" => ElementTheme.Dark,
                _ => ElementTheme.Default
            };
        }
    }

    private static async Task ShowErrorDialogAsync(string message)
    {
        if (App.MainWindow?.Content?.XamlRoot is { } xamlRoot)
        {
            var dialog = new Microsoft.UI.Xaml.Controls.ContentDialog
            {
                Title = "操作失败",
                Content = message,
                CloseButtonText = "确定",
                XamlRoot = xamlRoot
            };
            await dialog.ShowAsync();
        }
    }
}
