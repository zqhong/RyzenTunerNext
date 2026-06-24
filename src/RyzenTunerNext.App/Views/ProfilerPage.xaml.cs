using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RyzenTunerNext.App.ViewModels;

namespace RyzenTunerNext.App.Views;

public sealed partial class ProfilerPage : Page
{
    private ProfilerViewModel ViewModel => (ProfilerViewModel)DataContext;

    public ProfilerPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.LoadHistoryCommand.ExecuteAsync(null);
    }
}
