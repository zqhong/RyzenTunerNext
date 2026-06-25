using System.Diagnostics;
using System.Security.Principal;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RyzenTunerNext.App.Helpers;
using RyzenTunerNext.Core.Data;
using RyzenTunerNext.Core.Messaging;
using RyzenTunerNext.Core.Services;

namespace RyzenTunerNext.App;

public partial class App : Application
{
    private const string AppMutexName = "Global\\RyzenTunerNext_SingleInstance";
    private static Mutex? _appMutex;

    public static string ConnectionString { get; private set; } = string.Empty;
    public static SettingsRepository Settings { get; private set; } = null!;
    public static LogRepository Logs { get; private set; } = null!;
    public static ProfilerResultRepository ProfilerResults { get; private set; } = null!;
    public static StatusCacheRepository StatusCache { get; private set; } = null!;
    public static RyzenAdjWrapper RyzenAdj { get; private set; } = null!;
    public static PipeClient PipeClient { get; private set; } = null!;

    internal static MainWindow? MainWindow { get; private set; }

    internal static void SetMainWindow(MainWindow window)
    {
        MainWindow = window;
    }

    private CancellationTokenSource? _pipeClientCts;

    public App()
    {
        InitializeComponent();
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        // 0. 多实例保护
        _appMutex = new Mutex(true, AppMutexName, out var createdNew);
        if (!createdNew)
        {
            // 已有实例在运行，直接退出
            Application.Current.Exit();
            return;
        }

        // 1. 检查管理员权限
        if (!IsRunningAsAdmin())
        {
            await ShowAdminRequiredDialogAsync();
            return;
        }

        // 2. 初始化数据库
        var dbPath = Path.Combine(AppContext.BaseDirectory, "RyzenTunerNext.db");
        ConnectionString = $"Data Source={dbPath}";
        DatabaseInitializer.Initialize(ConnectionString);

        Settings = new SettingsRepository(ConnectionString);
        Logs = new LogRepository(ConnectionString);
        ProfilerResults = new ProfilerResultRepository(ConnectionString);
        StatusCache = new StatusCacheRepository(ConnectionString);
        RyzenAdj = new RyzenAdjWrapper();

        // 3. 检查反作弊警告
        await ShowAntiCheatWarningIfNeededAsync();

        // 4. 自动安装并启动 Service
        await EnsureServiceInstalledAndRunningAsync();

        // 5. 初始化 Pipe Client
        var loggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(builder => { });
        PipeClient = new PipeClient(loggerFactory.CreateLogger<PipeClient>());

        // 启动 Pipe Client 连接
        _pipeClientCts = new CancellationTokenSource();
        PipeClient.Start(_pipeClientCts.Token);

        // 6. 创建主窗口
        MainWindow = new MainWindow();
        MainWindow.Activate();
    }

    private async Task EnsureServiceInstalledAndRunningAsync()
    {
        // 检查 Service exe 是否存在
        var expectedPath = ServiceManager.GetServiceExePath();
        if (string.IsNullOrEmpty(expectedPath))
        {
            Logger.LogWarning("找不到 RyzenTunerNext.Service.exe，跳过 Service 自动安装");
            return;
        }

        var state = ServiceManager.GetServiceState();

        if (!state.IsInstalled)
        {
            // 未安装 → 安装
            var installResult = await ServiceManager.InstallAsync();
            if (!installResult.Success)
            {
                Logger.LogWarning("Service 安装失败: {Message}", installResult.Message);
                return;
            }
        }
        else
        {
            // 已安装 → 检查路径是否一致
            var installedPath = ServiceManager.GetInstalledServiceExePath();
            if (!string.IsNullOrEmpty(installedPath) &&
                !string.Equals(installedPath, expectedPath, StringComparison.OrdinalIgnoreCase))
            {
                // 路径不一致（用户更新了解压目录），重新安装
                Logger.LogInformation("Service 路径变更，重新安装: {Old} -> {New}", installedPath, expectedPath);
                await ServiceManager.UninstallAsync();
                var reinstallResult = await ServiceManager.InstallAsync();
                if (!reinstallResult.Success)
                {
                    Logger.LogWarning("Service 重新安装失败: {Message}", reinstallResult.Message);
                    return;
                }
            }
        }

        // 重新查询状态后启动（路径变更重新安装后状态可能已变化）
        state = ServiceManager.GetServiceState();
        if (state.IsInstalled && !state.IsRunning)
        {
            var startResult = await ServiceManager.StartAsync();
            if (!startResult.Success)
            {
                Logger.LogWarning("Service 启动失败: {Message}", startResult.Message);
            }
        }
    }

    private static readonly ILogger Logger = LoggerFactory.Create(builder => { }).CreateLogger<App>();

    private static bool IsRunningAsAdmin()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static async Task ShowAdminRequiredDialogAsync()
    {
        // 创建一个临时窗口来承载 ContentDialog
        var tempWindow = new Window();
        var grid = new Grid();
        tempWindow.Content = grid;
        tempWindow.Title = "RyzenTunerNext";
        tempWindow.Activate();

        // 等待 XamlRoot 就绪
        await Task.Delay(100);

        var dialog = new ContentDialog
        {
            Title = "需要管理员权限",
            Content = "RyzenTunerNext 需要管理员权限才能正常工作。\n\n请点击\"以管理员重新启动\"以提升权限。",
            PrimaryButtonText = "以管理员重新启动",
            CloseButtonText = "退出",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = grid.XamlRoot
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            // 以管理员重新启动
            var exePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
            if (exePath != null)
            {
                var psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    Verb = "runas",
                    UseShellExecute = true
                };
                try
                {
                    Process.Start(psi);
                }
                catch
                {
                    // 用户取消了 UAC 提示
                }
            }
        }

        // 退出当前实例
        tempWindow.Close();
        Application.Current.Exit();
    }

    private async Task ShowAntiCheatWarningIfNeededAsync()
    {
        var alreadyShown = await Settings.GetAntiCheatWarningShownAsync();
        if (alreadyShown) return;

        var dialog = new ContentDialog
        {
            Title = "反作弊软件兼容性提示",
            Content = "RyzenTunerNext 使用 WinRing0 内核驱动与 AMD SMU 通信。\n\n" +
                      "某些反作弊软件（如 Vanguard、EasyAntiCheat）和杀毒软件可能会拦截或标记此驱动。\n\n" +
                      "如果遇到问题，请将 RyzenTunerNext 目录添加到杀毒软件的白名单中。\n\n" +
                      "本工具不包含 inpoutx64.dll（已被反作弊广泛标记）。",
            CloseButtonText = "我已了解",
            DefaultButton = ContentDialogButton.Close
        };

        // 需要在窗口激活后才能显示
        // 延迟到主窗口创建后
        _antiCheatDialog = dialog;
        _showAntiCheatDialog = true;
    }

    // 在 MainWindow 激活后显示反作弊弹窗
    internal ContentDialog? _antiCheatDialog;
    internal bool _showAntiCheatDialog;

    internal async Task ShowPendingDialogsAsync(XamlRoot xamlRoot)
    {
        if (_showAntiCheatDialog && _antiCheatDialog != null)
        {
            _antiCheatDialog.XamlRoot = xamlRoot;
            await _antiCheatDialog.ShowAsync();
            await Settings.SetAsync("anti_cheat_warning_shown", "true");
            _antiCheatDialog = null;
            _showAntiCheatDialog = false;
        }
    }
}
