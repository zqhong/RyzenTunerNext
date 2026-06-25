using CommunityToolkit.Mvvm.ComponentModel;

namespace RyzenTunerNext.App.ViewModels;

public partial class AboutViewModel : ObservableObject
{
    [ObservableProperty] public partial string Version { get; set; } = "1.0.0";
    [ObservableProperty] public partial string EngineVersion { get; set; } = "v0.19.0";
    [ObservableProperty] public partial string CpuFamily { get; set; } = "检测中...";
    [ObservableProperty] public partial bool PowerManagerRunning { get; set; }
    [ObservableProperty] public partial bool RyzenAdjInitialized { get; set; }
    [ObservableProperty] public partial string Framework { get; set; } = ".NET 10 + WinUI 3";

    public void Refresh()
    {
        RyzenAdjInitialized = App.RyzenAdj.IsInitialized;
        if (RyzenAdjInitialized)
        {
            CpuFamily = App.RyzenAdj.GetCpuFamily().ToString();
        }

        // 单进程模式下 PowerManager 始终运行
        PowerManagerRunning = true;
    }
}
