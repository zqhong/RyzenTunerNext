using CommunityToolkit.Mvvm.ComponentModel;

namespace RyzenTunerNext.App.ViewModels;

public partial class AboutViewModel : ObservableObject
{
    [ObservableProperty] private string _version = "1.0.0";
    [ObservableProperty] private string _engineVersion = "v0.19.0";
    [ObservableProperty] private string _cpuFamily = "检测中...";
    [ObservableProperty] private bool _serviceRunning;
    [ObservableProperty] private bool _ryzenAdjInitialized;
    [ObservableProperty] private string _framework = ".NET 8 + WinUI 3";

    public void Refresh()
    {
        RyzenAdjInitialized = App.RyzenAdj.IsInitialized;
        if (RyzenAdjInitialized)
        {
            CpuFamily = App.RyzenAdj.GetCpuFamily().ToString();
        }
    }
}
