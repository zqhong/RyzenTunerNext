using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RyzenTunerNext.Core.Models;

namespace RyzenTunerNext.App.ViewModels;

public partial class LogViewModel : ObservableObject
{
    public ObservableCollection<LogEntry> Logs { get; } = new();

    [ObservableProperty] public partial string? SelectedLevel { get; set; }
    [ObservableProperty] public partial string? SearchText { get; set; }
    [ObservableProperty] public partial bool IsLoading { get; set; }

    public string[] Levels { get; } = ["所有", "Info", "Warning", "Error"];

    [RelayCommand]
    private async Task LoadLogsAsync()
    {
        IsLoading = true;
        try
        {
            Logs.Clear();
            var level = SelectedLevel == "所有" ? null : SelectedLevel;
            var logs = await App.Logs.QueryAsync(level, SearchText, limit: 500);
            foreach (var log in logs)
            {
                Logs.Add(log);
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task CleanupAsync()
    {
        var days = await App.Settings.GetLogRetentionDaysAsync();
        await App.Logs.CleanupOlderThanAsync(days);
        await LoadLogsAsync();
    }
}
