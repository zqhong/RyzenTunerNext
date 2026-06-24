using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RyzenTunerNext.App.ViewModels;

namespace RyzenTunerNext.App.Views;

public sealed partial class SettingsPage : Page
{
    private SettingsViewModel ViewModel => (SettingsViewModel)DataContext;

    public SettingsPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.LoadAsync();
    }
}
