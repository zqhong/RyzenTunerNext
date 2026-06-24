using Microsoft.UI.Xaml;
using RyzenTunerNext.Core.Data;
using RyzenTunerNext.Core.Messaging;
using RyzenTunerNext.Core.Services;
using RyzenTunerNext.App.ViewModels;

namespace RyzenTunerNext.App;

public partial class App : Application
{
    public static string ConnectionString { get; private set; } = string.Empty;
    public static SettingsRepository Settings { get; private set; } = null!;
    public static LogRepository Logs { get; private set; } = null!;
    public static ProfilerResultRepository ProfilerResults { get; private set; } = null!;
    public static RyzenAdjWrapper RyzenAdj { get; private set; } = null!;
    public static PipeClient PipeClient { get; private set; } = null!;

    private Window? _window;
    private CancellationTokenSource? _pipeClientCts;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // 初始化数据库
        var dbPath = Path.Combine(AppContext.BaseDirectory, "RyzenTunerNext.db");
        ConnectionString = $"Data Source={dbPath};Version=3;Journal Mode=WAL;";
        DatabaseInitializer.Initialize(ConnectionString);

        Settings = new SettingsRepository(ConnectionString);
        Logs = new LogRepository(ConnectionString);
        ProfilerResults = new ProfilerResultRepository(ConnectionString);
        RyzenAdj = new RyzenAdjWrapper();

        // 初始化 Pipe Client
        var loggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(builder => { });
        PipeClient = new PipeClient(loggerFactory.CreateLogger<PipeClient>());

        // 启动 Pipe Client 连接
        _pipeClientCts = new CancellationTokenSource();
        PipeClient.Start(_pipeClientCts.Token);

        _window = new MainWindow();
        _window.Activate();
    }
}
