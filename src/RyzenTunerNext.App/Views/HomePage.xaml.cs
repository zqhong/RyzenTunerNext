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
        App.PipeClient.MessageReceived += OnMessageReceived;
        App.PipeClient.ConnectionChanged += OnConnectionChanged;
        ViewModel.IsConnected = App.PipeClient.IsConnected;

        // 加载当前模式
        _ = LoadCurrentModeAsync();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        App.PipeClient.MessageReceived -= OnMessageReceived;
        App.PipeClient.ConnectionChanged -= OnConnectionChanged;
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

    private void OnConnectionChanged(object? sender, bool connected)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            ViewModel.IsConnected = connected;
        });
    }

    private void OnMessageReceived(object? sender, PipeMessage message)
    {
        if (message is StatusUpdateMessage statusMsg)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                ViewModel.UpdateFromStatus(statusMsg);
            });
        }
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
}
