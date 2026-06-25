using RyzenTunerNext.Core.Data;
using RyzenTunerNext.Core.Messaging;
using RyzenTunerNext.Core.Services;
using RyzenTunerNext.Service;

// 解析 --db-path 参数（由 App 通过 sc.exe binPath 传入）
var dbPath = ParseDbPath(args) ?? Path.Combine(AppContext.BaseDirectory, "RyzenTunerNext.db");
var connectionString = $"Data Source={dbPath}";

var builder = Host.CreateDefaultBuilder(args)
    .UseWindowsService(options =>
    {
        options.ServiceName = "RyzenTunerNext";
    })
    .ConfigureServices((hostContext, services) =>
    {
        // 数据库
        DatabaseInitializer.Initialize(connectionString);

        // 注册服务
        services.AddSingleton(new SettingsRepository(connectionString));
        services.AddSingleton(new LogRepository(connectionString));
        services.AddSingleton(new StatusCacheRepository(connectionString));
        services.AddSingleton<RyzenAdjWrapper>();
        services.AddSingleton<PipeServer>();
        services.AddSingleton<SystemEventMonitor>();
        services.AddSingleton<ModeScheduler>();
        services.AddSingleton<ParameterApplier>();
        services.AddHostedService<Worker>();
    });

var host = builder.Build();
await host.RunAsync();

/// <summary>
/// 从命令行参数中解析 --db-path 的值。
/// 支持格式：--db-path "C:\path\to\db" 或 --db-path C:\path\to\db
/// </summary>
static string? ParseDbPath(string[] args)
{
    for (int i = 0; i < args.Length - 1; i++)
    {
        if (args[i] == "--db-path")
        {
            return args[i + 1].Trim('"');
        }
    }
    return null;
}
