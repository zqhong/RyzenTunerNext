using RyzenTunerNext.Core.Data;
using RyzenTunerNext.Core.Messaging;
using RyzenTunerNext.Core.Services;
using RyzenTunerNext.Service;

var builder = Host.CreateDefaultBuilder(args)
    .UseWindowsService(options =>
    {
        options.ServiceName = "RyzenTunerNext";
    })
    .ConfigureServices((hostContext, services) =>
    {
        // 数据库
        var dbPath = Path.Combine(AppContext.BaseDirectory, "RyzenTunerNext.db");
        var connectionString = $"Data Source={dbPath};Version=3;Journal Mode=WAL;";
        DatabaseInitializer.Initialize(connectionString);

        // 注册服务
        services.AddSingleton(new SettingsRepository(connectionString));
        services.AddSingleton(new LogRepository(connectionString));
        services.AddSingleton<RyzenAdjWrapper>();
        services.AddSingleton<PipeServer>();
        services.AddSingleton<SystemEventMonitor>();
        services.AddSingleton<ModeScheduler>();
        services.AddSingleton<ParameterApplier>();
        services.AddHostedService<Worker>();
    });

var host = builder.Build();
await host.RunAsync();
