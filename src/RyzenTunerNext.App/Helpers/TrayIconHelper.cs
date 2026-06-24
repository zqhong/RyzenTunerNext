using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RyzenTunerNext.Core.Messaging;

namespace RyzenTunerNext.App.Helpers;

/// <summary>
/// 系统托盘图标管理器。
/// 负责右键菜单内容更新、最小化/恢复逻辑。
/// </summary>
internal class TrayIconHelper
{
    private readonly Window _window;
    private bool _isMinimizedToTray;

    // 右键菜单中的只读状态项
    private TextBlock? _serviceStatusText;
    private TextBlock? _currentModeText;
    private TextBlock? _currentLimitsText;
    private TextBlock? _currentMetricsText;

    // 模式切换 RadioButtons
    private RadioButton? _autoRadio;
    private RadioButton? _powerSavingRadio;
    private RadioButton? _performanceRadio;

    public event EventHandler? RestoreRequested;
    public event EventHandler<string>? ModeChangeRequested;
    public event EventHandler? ExitRequested;

    public TrayIconHelper(Window window)
    {
        _window = window;
    }

    /// <summary>
    /// 创建右键菜单内容
    /// </summary>
    public StackPanel CreateContextMenu()
    {
        var panel = new StackPanel { Spacing = 4, Padding = new Thickness(8) };

        // 只读状态项
        _serviceStatusText = new TextBlock
        {
            Text = "Service 状态: 检测中...",
            FontSize = 12,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
        };
        _currentModeText = new TextBlock
        {
            Text = "当前模式: --",
            FontSize = 12,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
        };
        _currentLimitsText = new TextBlock
        {
            Text = "参数: --",
            FontSize = 12,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
        };
        _currentMetricsText = new TextBlock
        {
            Text = "CPU: -- | 功耗: --",
            FontSize = 12,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
        };

        panel.Children.Add(_serviceStatusText);
        panel.Children.Add(_currentModeText);
        panel.Children.Add(_currentLimitsText);
        panel.Children.Add(_currentMetricsText);

        // 分隔线
        panel.Children.Add(new MenuFlyoutSeparator());

        // 模式切换
        _autoRadio = new RadioButton { Content = "自动", Tag = "Auto", GroupName = "TrayMode" };
        _powerSavingRadio = new RadioButton { Content = "省电模式", Tag = "PowerSaving", GroupName = "TrayMode" };
        _performanceRadio = new RadioButton { Content = "性能模式", Tag = "Performance", GroupName = "TrayMode" };

        _autoRadio.Checked += OnModeRadioChecked;
        _powerSavingRadio.Checked += OnModeRadioChecked;
        _performanceRadio.Checked += OnModeRadioChecked;

        panel.Children.Add(_autoRadio);
        panel.Children.Add(_powerSavingRadio);
        panel.Children.Add(_performanceRadio);

        // 分隔线
        panel.Children.Add(new MenuFlyoutSeparator());

        // 打开主窗口
        var restoreButton = new Button
        {
            Content = "打开主窗口",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Style = (Style)Application.Current.Resources["DefaultButtonStyle"]
        };
        restoreButton.Click += (_, _) => RestoreRequested?.Invoke(this, EventArgs.Empty);
        panel.Children.Add(restoreButton);

        // 退出
        var exitButton = new Button
        {
            Content = "退出",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Style = (Style)Application.Current.Resources["DefaultButtonStyle"]
        };
        exitButton.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);
        panel.Children.Add(exitButton);

        return panel;
    }

    private void OnModeRadioChecked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb && rb.Tag is string mode)
        {
            ModeChangeRequested?.Invoke(this, mode);
        }
    }

    /// <summary>
    /// 从 StatusUpdateMessage 更新托盘菜单状态
    /// </summary>
    public void UpdateFromStatus(StatusUpdateMessage msg)
    {
        var modeText = msg.Mode switch
        {
            "Auto" => "自动",
            "PowerSaving" => "省电模式",
            "Performance" => "性能模式",
            _ => msg.Mode
        };

        if (_currentModeText != null)
            _currentModeText.Text = $"当前模式: {modeText}";

        // 显示 Fast Limit 设置值 vs 实际值
        if (msg.SetLimits != null && msg.ActualLimits != null && _currentLimitsText != null)
        {
            var fastSet = msg.SetLimits.FastLimit / 1000.0;
            var fastActual = msg.ActualLimits.FastLimit / 1000.0;
            _currentLimitsText.Text = $"Fast Limit: {fastSet:F0}W → 实际 {fastActual:F1}W";
        }
        else if (msg.SetLimits != null && _currentLimitsText != null)
        {
            var fastW = msg.SetLimits.FastLimit / 1000.0;
            _currentLimitsText.Text = $"Fast Limit: {fastW:F0}W";
        }

        if (msg.ActualLimits != null && _currentMetricsText != null)
        {
            var power = msg.ActualLimits.SocketPower / 1000.0;
            _currentMetricsText.Text = $"CPU 温度: {msg.ActualLimits.CpuTemp:F0}℃ | 功耗: {power:F1}W";
        }

        // 更新模式 RadioButtons（避免循环触发）
        UpdateModeRadioSilent(msg.Mode);
    }

    /// <summary>
    /// 更新 Service 连接状态
    /// </summary>
    public void UpdateServiceStatus(bool connected)
    {
        if (_serviceStatusText != null)
            _serviceStatusText.Text = connected ? "Service 状态: 运行中" : "Service 状态: 未连接";
    }

    /// <summary>
    /// 更新当前模式（不触发事件）
    /// </summary>
    public void UpdateMode(string mode)
    {
        UpdateModeRadioSilent(mode);

        var modeText = mode switch
        {
            "Auto" => "自动",
            "PowerSaving" => "省电模式",
            "Performance" => "性能模式",
            _ => mode
        };
        if (_currentModeText != null)
            _currentModeText.Text = $"当前模式: {modeText}";
    }

    private void UpdateModeRadioSilent(string mode)
    {
        var target = mode switch
        {
            "Auto" => _autoRadio,
            "PowerSaving" => _powerSavingRadio,
            "Performance" => _performanceRadio,
            _ => _autoRadio
        };

        if (target != null && target.IsChecked != true)
        {
            // 临时移除事件处理避免循环
            _autoRadio!.Checked -= OnModeRadioChecked;
            _powerSavingRadio!.Checked -= OnModeRadioChecked;
            _performanceRadio!.Checked -= OnModeRadioChecked;

            target.IsChecked = true;

            _autoRadio.Checked += OnModeRadioChecked;
            _powerSavingRadio.Checked += OnModeRadioChecked;
            _performanceRadio.Checked += OnModeRadioChecked;
        }
    }

    /// <summary>
    /// 生成 Tooltip 文本
    /// </summary>
    public static string GetTooltipText(string mode, double power, double temp)
    {
        var modeText = mode switch
        {
            "Auto" => "自动",
            "PowerSaving" => "省电",
            "Performance" => "性能",
            _ => mode
        };
        return $"RyzenTunerNext | {modeText} | {power:F1}W | {temp:F0}℃";
    }

    /// <summary>
    /// 最小化到托盘
    /// </summary>
    public void MinimizeToTray()
    {
        _isMinimizedToTray = true;
        var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(_window);
        var windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);
        appWindow.Hide();
    }

    /// <summary>
    /// 从托盘恢复
    /// </summary>
    public void RestoreFromTray()
    {
        _isMinimizedToTray = false;
        var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(_window);
        var windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);
        appWindow.Show();

        // 激活窗口到前台
        SetForegroundWindow(hWnd);
    }

    public bool IsMinimizedToTray => _isMinimizedToTray;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
}
