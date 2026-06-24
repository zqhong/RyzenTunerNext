using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RyzenTunerNext.App.ViewModels;

namespace RyzenTunerNext.App.Views;

public sealed partial class LogPage : Page
{
    private LogViewModel ViewModel => (LogViewModel)DataContext;

    public LogPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.LoadLogsCommand.ExecuteAsync(null);
    }

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        ViewModel.SearchText = sender.Text;
        _ = ViewModel.LoadLogsCommand.ExecuteAsync(null);
    }

    private void LevelFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LevelFilter.SelectedItem is string level)
        {
            ViewModel.SelectedLevel = level;
            _ = ViewModel.LoadLogsCommand.ExecuteAsync(null);
        }
    }
}
