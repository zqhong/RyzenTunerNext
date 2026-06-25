using Microsoft.Extensions.Logging;
using RyzenTunerNext.Core.Data;

namespace RyzenTunerNext.App.Services;

/// <summary>
/// 将 Microsoft.Extensions.Logging 的日志桥接到 SQLite LogRepository。
/// 解决 LoggerFactory 无 provider 导致 ILogger 日志全部丢弃的问题。
/// </summary>
public sealed class LogRepositoryLoggerProvider : ILoggerProvider
{
    private readonly LogRepository _logs;

    public LogRepositoryLoggerProvider(LogRepository logs)
    {
        _logs = logs;
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new LogRepositoryLogger(_logs, categoryName);
    }

    public void Dispose() { }

    private sealed class LogRepositoryLogger : ILogger
    {
        private readonly LogRepository _logs;
        private readonly string _category;

        public LogRepositoryLogger(LogRepository logs, string category)
        {
            _logs = logs;
            _category = category;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel)
        {
            // 只记录 Information 及以上级别，忽略 Debug/Trace
            return logLevel >= LogLevel.Information;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;

            var message = formatter(state, exception);
            var source = ShortenCategory(_category);
            var level = logLevel switch
            {
                LogLevel.Warning => "Warning",
                LogLevel.Error => "Error",
                LogLevel.Critical => "Error",
                _ => "Info"
            };
            var detail = exception?.ToString();

            // ILogger.Log 是同步接口，LogRepository 是异步 → fire-and-forget
            // 日志失败不应影响主逻辑
            _ = Task.Run(async () =>
            {
                try
                {
                    await _logs.InsertAsync(level, source, message, detail);
                }
                catch
                {
                    // 静默吞掉：日志写入失败不应导致应用崩溃
                }
            });
        }

        /// <summary>
        /// 将完整分类名缩短为简短类名，如 "RyzenTunerNext.App.Services.PowerManager" → "PowerManager"
        /// </summary>
        private static string ShortenCategory(string category)
        {
            var lastDot = category.LastIndexOf('.');
            return lastDot >= 0 ? category[(lastDot + 1)..] : category;
        }
    }
}
