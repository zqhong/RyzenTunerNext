using Dapper;
using System.Data;
using Microsoft.Data.Sqlite;

namespace RyzenTunerNext.Core.Data;

/// <summary>
/// 配置数据访问 (settings 表, KV 存储)
/// </summary>
public class SettingsRepository
{
    private readonly string _connectionString;

    public SettingsRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    private IDbConnection CreateConnection() => new SqliteConnection(_connectionString);

    public async Task<string?> GetAsync(string key)
    {
        using var db = CreateConnection();
        return await db.QuerySingleOrDefaultAsync<string?>(
            "SELECT value FROM settings WHERE key = @Key", new { Key = key });
    }

    public async Task<int> GetIntAsync(string key, int defaultValue)
    {
        var value = await GetAsync(key);
        return value != null && int.TryParse(value, out var result) ? result : defaultValue;
    }

    public async Task<bool> GetBoolAsync(string key, bool defaultValue)
    {
        var value = await GetAsync(key);
        return value != null && bool.TryParse(value, out var result) ? result : defaultValue;
    }

    public async Task SetAsync(string key, string value)
    {
        using var db = CreateConnection();
        await db.ExecuteAsync(
            "INSERT OR REPLACE INTO settings (key, value) VALUES (@Key, @Value)",
            new { Key = key, Value = value });
    }

    // ===== 便捷方法 =====

    public async Task<string> GetEnergyModeAsync()
        => await GetAsync("energy_mode") ?? "Auto";

    public async Task<int> GetFastLimitPerformanceAsync()
        => await GetIntAsync("fast_limit_performance", 45000);

    public async Task<int> GetSlowLimitPerformanceAsync()
        => await GetIntAsync("slow_limit_performance", 45000);

    public async Task<int> GetFastLimitPowersavingAsync()
        => await GetIntAsync("fast_limit_powersaving", 25000);

    public async Task<int> GetSlowLimitPowersavingAsync()
        => await GetIntAsync("slow_limit_powersaving", 15000);

    public async Task<int> GetTctlTempAsync()
        => await GetIntAsync("tctl_temp", 90);

    public async Task<int> GetApplyIntervalAsync()
        => await GetIntAsync("apply_interval", 4000);

    public async Task<int> GetLogRetentionDaysAsync()
        => await GetIntAsync("log_retention_days", 30);

    public async Task<long> GetAutoIdleTimeoutAsync()
        => await GetIntAsync("auto_idle_timeout", 300000);

    public async Task<int> GetAutoCpuThresholdAsync()
        => await GetIntAsync("auto_cpu_threshold", 10);

    public async Task<bool> GetAntiCheatWarningShownAsync()
        => await GetBoolAsync("anti_cheat_warning_shown", false);
}
