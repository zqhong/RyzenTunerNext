using System.Diagnostics;
using System.Security.Principal;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RyzenTunerNext.App.Services;
using RyzenTunerNext.Core.Data;
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
    public static PowerManager PowerManager { get; private set; } = null!;

    internal static MainWindow? MainWindow { get; private set; }

    internal static void SetMainWindow(MainWindow window)
    {
        MainWindow = window;
    }

    public App()
    {
        // 全局异常捕获 — 记录未处理的异常，避免进程静默退出
        UnhandledException += (_, e) =>
        {
            DiagnosticFileLogger.Write($"[UnhandledException] {e.Exception}");
            DiagnosticFileLogger.Write($"[UnhandledException] Handled: {e.Handled}");
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            DiagnosticFileLogger.Write($"[UnobservedTaskException] {e.Exception}");
            e.SetObserved();
        };

        // 尽早初始化原生库搜索路径（native/ 子目录）
        // 必须在任何 P/Invoke 调用之前
        NativeLibraryLoader.Initialize();

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

        DiagnosticFileLogger.Write("OnLaunched 开始");

        try
        {
            // 1. 检查管理员权限
            DiagnosticFileLogger.Write("步骤 1: 检查管理员权限");
            if (!IsRunningAsAdmin())
            {
                DiagnosticFileLogger.Write("非管理员，弹出提权对话框");
                await ShowAdminRequiredDialogAsync();
                return;
            }
            DiagnosticFileLogger.Write("管理员权限 OK");

            // 2. 初始化数据库
            DiagnosticFileLogger.Write("步骤 2: 初始化数据库");
            var dbPath = Path.Combine(AppContext.BaseDirectory, "RyzenTunerNext.db");
            ConnectionString = $"Data Source={dbPath}";
            DatabaseInitializer.Initialize(ConnectionString);

            Settings = new SettingsRepository(ConnectionString);
            Logs = new LogRepository(ConnectionString);
            ProfilerResults = new ProfilerResultRepository(ConnectionString);
            StatusCache = new StatusCacheRepository(ConnectionString);
            RyzenAdj = new RyzenAdjWrapper();

            await Logs.InfoAsync("App", "数据库初始化完成");
            DiagnosticFileLogger.Write("数据库初始化完成");

            // 3. 检查反作弊警告
            DiagnosticFileLogger.Write("步骤 3: 检查反作弊警告");
            await ShowAntiCheatWarningIfNeededAsync();
            DiagnosticFileLogger.Write("反作弊警告检查完成");

            // 4. 构造 PowerManager（MainWindow 构造函数需要订阅其事件）
            //    ILogger 日志通过 LogRepositoryLoggerProvider 写入 SQLite
            DiagnosticFileLogger.Write("步骤 4: 构造 PowerManager");
            var loggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(builder =>
            {
                builder.AddProvider(new LogRepositoryLoggerProvider(Logs));
                builder.SetMinimumLevel(LogLevel.Information);
            });
            PowerManager = new PowerManager(
                RyzenAdj,
                Settings,
                Logs,
                StatusCache,
                new ParameterApplier(RyzenAdj, Settings, Logs, loggerFactory.CreateLogger<ParameterApplier>()),
                new ModeScheduler(Settings, Logs, loggerFactory.CreateLogger<ModeScheduler>()),
                new SystemEventMonitor(loggerFactory.CreateLogger<SystemEventMonitor>()),
                loggerFactory.CreateLogger<PowerManager>());

            await Logs.InfoAsync("App", "PowerManager 已构造，开始后台初始化");
            DiagnosticFileLogger.Write("PowerManager 构造完成");

            // 5. 创建主窗口（必须在 PowerManager 构造之后，因为 MainWindow 订阅其事件）
            DiagnosticFileLogger.Write("步骤 5: 创建 MainWindow");
            MainWindow = new MainWindow();
            DiagnosticFileLogger.Write("MainWindow 构造完成");
            MainWindow.Activate();
            DiagnosticFileLogger.Write("MainWindow 已激活");

            // 6. 后台启动 PowerManager 主循环（fire-and-forget，不阻塞 UI）
            //    内部的 InitializeRyzenAdjAsync 可能有重试（最多 10 次 × 30 秒），
            //    不 await 以避免界面白屏
            DiagnosticFileLogger.Write("步骤 6: 启动 PowerManager 后台任务");
            _ = Task.Run(async () =>
            {
                try
                {
                    DiagnosticFileLogger.Write("PowerManager.StartAsync 开始（后台线程）");
                    await PowerManager.StartAsync(CancellationToken.None);
                    DiagnosticFileLogger.Write("PowerManager.StartAsync 完成");
                }
                catch (Exception ex)
                {
                    DiagnosticFileLogger.Write($"PowerManager.StartAsync 异常: {ex}");
                    await Logs.ErrorAsync("App", $"PowerManager 启动失败: {ex.Message}", ex.StackTrace);
                }
            });

            DiagnosticFileLogger.Write("OnLaunched 完成");
        }
        catch (Exception ex)
        {
            DiagnosticFileLogger.Write($"OnLaunched 异常: {ex}");
            // 启动失败时展示错误窗口，避免进程静默运行但无界面
            await ShowStartupErrorDialogAsync(ex);
        }
    }

    private static async Task ShowStartupErrorDialogAsync(Exception ex)
    {
        var tempWindow = new Window();
        var grid = new Grid();
        tempWindow.Content = grid;
        tempWindow.Title = "RyzenTunerNext - 启动失败";
        tempWindow.Activate();

        await Task.Delay(100);

        var dialog = new ContentDialog
        {
            Title = "启动失败",
            Content = $"RyzenTunerNext 启动过程中发生错误：\n\n{ex.Message}",
            CloseButtonText = "退出",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = grid.XamlRoot
        };

        await dialog.ShowAsync();
        tempWindow.Close();
        Application.Current.Exit();
    }

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
