using System.Runtime.InteropServices;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using RyzenTunerNext.App.Helpers;
using RyzenTunerNext.App.Services;
using RyzenTunerNext.App.Views;
using RyzenTunerNext.Core.Messaging;

namespace RyzenTunerNext.App;

public sealed partial class MainWindow : Window
{
    private TrayIconHelper _trayHelper;

    // 窗口位置记忆
    private const string SettingKeyWindowLeft = "window_left";
    private const string SettingKeyWindowTop = "window_top";
    private const string SettingKeyWindowWidth = "window_width";
    private const string SettingKeyWindowHeight = "window_height";

    // 自动最小化到托盘
    private DispatcherTimer? _idleCheckTimer;
    private DateTime _lastUserInputTime = DateTime.Now;

    // 托盘 Tooltip 更新
    private string _currentMode = "Auto";
    private double _currentPower;
    private double _currentTemp;

    // 自动恢复窗口检测
    private string _previousMode = "Auto";

    /// <summary>
    /// 托盘图标双击恢复命令
    /// </summary>
    [RelayCommand]
    private void TrayRestore()
    {
        _trayHelper.RestoreFromTray();
    }

    public MainWindow()
    {
        DiagnosticFileLogger.Write("[MainWindow] InitializeComponent 开始");
        InitializeComponent();
        DiagnosticFileLogger.Write("[MainWindow] InitializeComponent 完成");

        Title = "RyzenTunerNext";

        // 设置窗口大小
        DiagnosticFileLogger.Write("[MainWindow] 设置窗口大小");
        var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);
        appWindow.Resize(new Windows.Graphics.SizeInt32(1000, 700));

        // 窗口关闭时清理资源
        this.Closed += (_, _) =>
        {
            DiagnosticFileLogger.Write("[MainWindow] Closed 事件触发");
            Cleanup();
        };

        // 初始化系统托盘
        DiagnosticFileLogger.Write("[MainWindow] 初始化系统托盘");
        _trayHelper = new TrayIconHelper(this);
        SetupTrayIcon();
        DiagnosticFileLogger.Write("[MainWindow] 系统托盘初始化完成");

        // 恢复窗口位置
        _ = RestoreWindowPositionAsync();

        // 注册 PowerManager 事件（用于更新托盘菜单）
        App.PowerManager.StatusUpdated += OnStatusUpdated;
        App.PowerManager.StateChanged += OnStateChanged;

        // 初始化空闲检测定时器
        SetupIdleCheckTimer();

        // 窗口激活后显示待处理弹窗
        Activated += OnWindowActivated;

        DiagnosticFileLogger.Write("[MainWindow] 构造函数完成");
    }

    #region 系统托盘

    private void SetupTrayIcon()
    {
        DiagnosticFileLogger.Write("[MainWindow] SetupTrayIcon 开始");

        // 设置图标（使用 ICO 格式，H.NotifyIcon 的 PNG→Icon 转换存在兼容性问题）
        var iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");
        DiagnosticFileLogger.Write($"[MainWindow] 图标路径: {iconPath}, 存在: {System.IO.File.Exists(iconPath)}");
        if (System.IO.File.Exists(iconPath))
        {
            var icon = new System.Drawing.Icon(iconPath, new System.Drawing.Size(16, 16));
            TrayIcon.Icon = icon;
            DiagnosticFileLogger.Write("[MainWindow] 图标设置成功");
        }

        // 设置右键菜单
        DiagnosticFileLogger.Write("[MainWindow] 创建右键菜单");
        var contextMenu = _trayHelper.CreateContextMenu();
        TrayIcon.ContextFlyout = new Flyout
        {
            Content = contextMenu,
            Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.Bottom
        };

        // 订阅托盘事件
        _trayHelper.RestoreRequested += (_, _) => _trayHelper.RestoreFromTray();
        _trayHelper.ModeChangeRequested += async (_, mode) =>
        {
            App.PowerManager.SetMode(mode);
            await App.Settings.SetAsync("energy_mode", mode);
        };
        _trayHelper.ExitRequested += (_, _) => Close();

        // 初始状态（单进程模式下始终为运行中）
        _trayHelper.UpdateServiceStatus(true);
        DiagnosticFileLogger.Write("[MainWindow] SetupTrayIcon 完成");
    }

    private void OnStatusUpdated(object? sender, StatusUpdateMessage statusMsg)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            _trayHelper.UpdateFromStatus(statusMsg);

            var previousMode = _currentMode;
            _currentMode = statusMsg.Mode;

            if (statusMsg.ActualLimits != null)
            {
                _currentPower = statusMsg.ActualLimits.SocketPower / 1000.0;
                _currentTemp = statusMsg.ActualLimits.CpuTemp;
            }
            UpdateTrayTooltip();

            // 自动恢复窗口：模式从省电切回性能时（用户变为活跃）
            if (_trayHelper.IsMinimizedToTray
                && previousMode == "PowerSaving"
                && _currentMode == "Performance")
            {
                _trayHelper.RestoreFromTray();
            }

            _previousMode = previousMode;
        });
    }

    private void OnStateChanged(object? sender, ServiceStateMessage stateMsg)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            _trayHelper.UpdateServiceStatus(stateMsg.IsRunning);
        });
    }

    private void UpdateTrayTooltip()
    {
        TrayIcon.ToolTipText = TrayIconHelper.GetTooltipText(_currentMode, _currentPower, _currentTemp);
    }

    #endregion

    #region 关闭到托盘

    private void Cleanup()
    {
        _idleCheckTimer?.Stop();
        App.PowerManager.StatusUpdated -= OnStatusUpdated;
        App.PowerManager.StateChanged -= OnStateChanged;
        TrayIcon.Dispose();
        _ = App.PowerManager.StopAsync();
    }

    #endregion

    #region 窗口位置记忆

    private async Task RestoreWindowPositionAsync()
    {
        try
        {
            var leftStr = await App.Settings.GetAsync(SettingKeyWindowLeft);
            var topStr = await App.Settings.GetAsync(SettingKeyWindowTop);
            var widthStr = await App.Settings.GetAsync(SettingKeyWindowWidth);
            var heightStr = await App.Settings.GetAsync(SettingKeyWindowHeight);

            if (int.TryParse(leftStr, out var left) && int.TryParse(topStr, out var top) &&
                int.TryParse(widthStr, out var width) && int.TryParse(heightStr, out var height))
            {
                // 验证位置在屏幕范围内
                if (left >= 0 && top >= 0 && width >= 800 && height >= 600)
                {
                    var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                    var windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
                    var appWindow = AppWindow.GetFromWindowId(windowId);

                    // 使用 Move 确保位置正确
                    appWindow.Move(new Windows.Graphics.PointInt32(left, top));
                    appWindow.Resize(new Windows.Graphics.SizeInt32(width, height));
                }
            }
        }
        catch
        {
            // 忽略恢复失败，使用默认位置
        }
    }

    private async Task SaveWindowPositionAsync()
    {
        try
        {
            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
            var appWindow = AppWindow.GetFromWindowId(windowId);
            var pos = appWindow.Position;
            var size = appWindow.Size;

            await App.Settings.SetAsync(SettingKeyWindowLeft, pos.X.ToString());
            await App.Settings.SetAsync(SettingKeyWindowTop, pos.Y.ToString());
            await App.Settings.SetAsync(SettingKeyWindowWidth, size.Width.ToString());
            await App.Settings.SetAsync(SettingKeyWindowHeight, size.Height.ToString());
        }
        catch
        {
            // 忽略保存失败
        }
    }

    #endregion

    #region 自动最小化到托盘

    private void SetupIdleCheckTimer()
    {
        _idleCheckTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(30)
        };
        _idleCheckTimer.Tick += OnIdleCheckTick;
        _idleCheckTimer.Start();
    }

    private void OnIdleCheckTick(object? sender, object e)
    {
        _ = CheckAutoMinimizeAsync();
    }

    private async Task CheckAutoMinimizeAsync()
    {
        // 检查条件：自动模式 + 电池供电 + 无用户输入 ≥ 5 分钟
        var mode = await App.Settings.GetEnergyModeAsync();
        if (mode != "Auto")
        {
            return;
        }

        // 仅电池供电时才自动最小化
        if (!IsOnBattery())
        {
            return;
        }

        // 检查用户空闲时间
        var idleTime = GetIdleTime();
        if (idleTime.TotalMinutes >= 5)
        {
            if (!_trayHelper.IsMinimizedToTray)
            {
                _trayHelper.MinimizeToTray();
            }
        }
    }

    /// <summary>
    /// 检查是否使用电池供电（AC 电源离线）
    /// </summary>
    private static bool IsOnBattery()
    {
        var status = new SYSTEM_POWER_STATUS();
        if (GetSystemPowerStatus(ref status))
        {
            // ACLineStatus: 0 = 离线(电池), 1 = 在线(AC), 255 = 未知
            return status.ACLineStatus == 0;
        }
        return false;
    }

    private static TimeSpan GetIdleTime()
    {
        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (GetLastInputInfo(ref info))
        {
            return TimeSpan.FromMilliseconds(Environment.TickCount - info.dwTime);
        }
        return TimeSpan.Zero;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte Reserved1;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    [DllImport("kernel32.dll")]
    private static extern bool GetSystemPowerStatus(ref SYSTEM_POWER_STATUS lpSystemPowerStatus);

    #endregion

    #region 导航和弹窗

    private void NavView_Loaded(object sender, RoutedEventArgs e)
    {
        DiagnosticFileLogger.Write("[MainWindow] NavView_Loaded 触发");
        NavView.SelectedItem = NavView.MenuItems[0];
        ContentFrame.Navigate(typeof(HomePage));
        DiagnosticFileLogger.Write("[MainWindow] HomePage 导航完成");
    }

    private void NavView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (args.InvokedItemContainer is NavigationViewItem item)
        {
            var tag = item.Tag?.ToString();
            NavigateToPage(tag);
        }
    }

    private void NavigateToPage(string? tag)
    {
        Type? pageType = tag switch
        {
            "Home" => typeof(HomePage),
            "Settings" => typeof(SettingsPage),
            "Profiler" => typeof(ProfilerPage),
            "Logs" => typeof(LogPage),
            "About" => typeof(AboutPage),
            _ => null
        };

        if (pageType != null && ContentFrame.CurrentSourcePageType != pageType)
        {
            ContentFrame.Navigate(pageType);
        }
    }

    private async void OnWindowActivated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState != WindowActivationState.Deactivated)
        {
            DiagnosticFileLogger.Write("[MainWindow] OnWindowActivated 首次激活");
            Activated -= OnWindowActivated;

            if (Content is FrameworkElement root)
            {
                DiagnosticFileLogger.Write("[MainWindow] 显示待处理弹窗");
                await ((App)Application.Current).ShowPendingDialogsAsync(root.XamlRoot);
                DiagnosticFileLogger.Write("[MainWindow] 待处理弹窗完成");
            }

            if (App.MainWindow == null)
            {
                App.SetMainWindow(this);
            }
            DiagnosticFileLogger.Write("[MainWindow] OnWindowActivated 完成");
        }
    }

    /// <summary>
    /// 窗口移动/缩放时保存位置
    /// </summary>
    internal async Task OnWindowBoundsChanged()
    {
        await SaveWindowPositionAsync();
    }

    #endregion
}
