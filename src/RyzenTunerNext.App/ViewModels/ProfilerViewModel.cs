using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RyzenTunerNext.Core.Models;
using RyzenTunerNext.Core.Services;

namespace RyzenTunerNext.App.ViewModels;

public partial class ProfilerViewModel : ObservableObject
{
    public ObservableCollection<ProfilerResult> Results { get; } = new();

    [ObservableProperty] public partial bool IsRunning { get; set; }
    [ObservableProperty] public partial string StatusText { get; set; } = "就绪";
    [ObservableProperty] public partial double Progress { get; set; }

    /// <summary>要测试的功耗档位 (mW)</summary>
    public int[] PowerLevels { get; } = [15000, 25000, 35000, 45000, 54000];

    [RelayCommand]
    private async Task RunMultiCoreTestAsync()
    {
        await RunTestAsync("MultiThread");
    }

    [RelayCommand]
    private async Task RunSingleCoreTestAsync()
    {
        await RunTestAsync("SingleThread");
    }

    private async Task RunTestAsync(string testType)
    {
        IsRunning = true;
        Results.Clear();

        var tctlTemp = await App.Settings.GetTctlTempAsync();
        var totalSteps = PowerLevels.Length;

        try
        {
            for (int i = 0; i < totalSteps; i++)
            {
                var power = PowerLevels[i];
                StatusText = $"测试中: {power / 1000}W ({i + 1}/{totalSteps})";
                Progress = (double)i / totalSteps * 100;

                // 设置功耗
                var profile = new PowerProfile
                {
                    Mode = EnergyMode.Performance,
                    FastLimit = power,
                    SlowLimit = power,
                    TctlTemp = tctlTemp
                };

                var applyResult = App.RyzenAdj.ApplyProfile(profile);

                // 等待稳定
                await Task.Delay(2000);

                // 运行基准测试
                long score;
                double elapsedMs;
                if (testType == "MultiThread")
                    (score, elapsedMs) = BenchmarkEngine.RunMultiCore();
                else
                    (score, elapsedMs) = BenchmarkEngine.RunSingleCore();

                // 读取实际指标
                var actual = App.RyzenAdj.ReadActualValues();
                var metrics = App.RyzenAdj.ReadCpuMetrics();

                var result = new ProfilerResult
                {
                    CreatedAt = DateTime.UtcNow.ToString("o"),
                    TestType = testType,
                    Score = score,
                    FastLimit = power,
                    SlowLimit = power,
                    TctlTemp = tctlTemp,
                    AvgFrequency = metrics.AvgFrequency,
                    AvgPower = actual.SocketPower,
                    MaxTemp = actual.TctlTempValue,
                    Efficiency = actual.SocketPower > 0 ? score / (actual.SocketPower / 1000.0) : 0
                };

                Results.Add(result);
                await App.ProfilerResults.InsertAsync(result);
            }

            Progress = 100;
            StatusText = "测试完成";
        }
        catch (Exception ex)
        {
            StatusText = $"测试失败: {ex.Message}";
        }
        finally
        {
            IsRunning = false;
        }
    }

    [RelayCommand]
    private async Task LoadHistoryAsync()
    {
        Results.Clear();
        var history = await App.ProfilerResults.GetAllAsync();
        foreach (var r in history)
        {
            Results.Add(r);
        }
    }

    [RelayCommand]
    private async Task ClearHistoryAsync()
    {
        await App.ProfilerResults.ClearAsync();
        Results.Clear();
    }
}
