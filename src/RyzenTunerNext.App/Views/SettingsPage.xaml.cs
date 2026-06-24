using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using RyzenTunerNext.App.ViewModels;
using Windows.System;

namespace RyzenTunerNext.App.Views;

public sealed partial class SettingsPage : Page
{
    private SettingsViewModel ViewModel => (SettingsViewModel)DataContext;
    private bool _isRecordingHotkey;

    public SettingsPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.LoadAsync();
    }

    #region 快捷键录制

    private void HotkeyBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb)
        {
            _isRecordingHotkey = true;
            tb.PlaceholderText = "请按下快捷键组合...";
        }
    }

    private void HotkeyBox_LostFocus(object sender, RoutedEventArgs e)
    {
        _isRecordingHotkey = false;
        if (sender is TextBox tb)
        {
            tb.PlaceholderText = "点击后按下快捷键组合...";
        }
    }

    private void HotkeyBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (!_isRecordingHotkey || sender is not TextBox tb) return;

        var key = e.Key;
        // 忽略单独的修饰键
        if (key is VirtualKey.Control or VirtualKey.Shift or VirtualKey.Menu or
            VirtualKey.LeftControl or VirtualKey.RightControl or
            VirtualKey.LeftShift or VirtualKey.RightShift or
            VirtualKey.LeftMenu or VirtualKey.RightMenu)
        {
            return;
        }

        // 获取当前按下的修饰键 (使用 InputKeyboardSource，WinUI 3 兼容)
        var ctrlState = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control);
        var shiftState = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift);
        var altState = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Menu);

        var parts = new List<string>();
        if (ctrlState.HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down))
            parts.Add("Ctrl");
        if (shiftState.HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down))
            parts.Add("Shift");
        if (altState.HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down))
            parts.Add("Alt");

        parts.Add(key.ToString());

        var hotkeyStr = string.Join("+", parts);
        tb.Text = hotkeyStr;

        // 更新对应的 ViewModel 属性
        var tag = tb.Tag?.ToString();
        switch (tag)
        {
            case "ToggleMode":
                ViewModel.HotkeyToggleMode = hotkeyStr;
                break;
            case "ApplyNow":
                ViewModel.HotkeyApplyNow = hotkeyStr;
                break;
            case "ShowWindow":
                ViewModel.HotkeyShowWindow = hotkeyStr;
                break;
        }

        e.Handled = true;
    }

    #endregion
}
