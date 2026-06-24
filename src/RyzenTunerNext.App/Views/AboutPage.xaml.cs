using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RyzenTunerNext.App.ViewModels;

namespace RyzenTunerNext.App.Views;

public sealed partial class AboutPage : Page
{
    private AboutViewModel ViewModel => (AboutViewModel)DataContext;

    public AboutPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ViewModel.Refresh();
    }
}
