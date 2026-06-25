using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RyzenTunerNext.App.ViewModels;
using RyzenTunerNext.Core.Messaging;

namespace RyzenTunerNext.App.Views;

public sealed partial class HomePage : Page
{
    private HomeViewModel ViewModel => (HomeViewModel)DataContext;

    public HomePage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        App.PowerManager.StatusUpdated += OnStatusUpdated;
        ViewModel.IsConnected = true; // 单进程模式下始终连接

        // 加载当前模式
        _ = LoadCurrentModeAsync();

        // 从 StatusCache 恢复上次状态（GUI 重启后恢复显示）
        _ = LoadCachedStatusAsync();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        App.PowerManager.StatusUpdated -= OnStatusUpdated;
    }

    private async Task LoadCurrentModeAsync()
    {
        var mode = await App.Settings.GetEnergyModeAsync();
        ViewModel.CurrentMode = mode;
        ModeComboBox.SelectedIndex = mode switch
        {
            "Auto" => 0,
            "PowerSaving" => 1,
            "Performance" => 2,
            _ => 0
        };
    }

    private void OnStatusUpdated(object? sender, StatusUpdateMessage statusMsg)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            ViewModel.UpdateFromStatus(statusMsg);
        });
    }

    private void ModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var mode = ModeComboBox.SelectedIndex switch
        {
            0 => "Auto",
            1 => "PowerSaving",
            2 => "Performance",
            _ => "Auto"
        };
        _ = ViewModel.SwitchModeCommand.ExecuteAsync(mode);
    }

    private void ApplyNow_Click(object sender, RoutedEventArgs e)
    {
        _ = ViewModel.ApplyNowCommand.ExecuteAsync(null);
    }

    /// <summary>
    /// 从 StatusCache 加载上次状态，GUI 重启后恢复显示。
    /// PipeClient 收到新数据后会自动覆盖。
    /// </summary>
    private async Task LoadCachedStatusAsync()
    {
        try
        {
            var json = await App.StatusCache.GetAsync("last_status");
            if (string.IsNullOrEmpty(json)) return;

            var cached = JsonSerializer.Deserialize<StatusUpdateMessage>(json);
            if (cached != null)
            {
                ViewModel.UpdateFromStatus(cached);
            }
        }
        catch
        {
            // 忽略缓存读取失败
        }
    }
}
