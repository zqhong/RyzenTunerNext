using Dapper;
using System.Data;
using Microsoft.Data.Sqlite;

namespace RyzenTunerNext.Core.Data;

/// <summary>
/// 状态缓存数据访问 (status_cache 表)。
/// 用于 GUI 重启后恢复状态显示。
/// </summary>
public class StatusCacheRepository
{
    private readonly string _connectionString;

    public StatusCacheRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    private IDbConnection CreateConnection() => new SqliteConnection(_connectionString);

    /// <summary>
    /// 更新缓存项
    /// </summary>
    public async Task SetAsync(string key, string value)
    {
        using var db = CreateConnection();
        await db.ExecuteAsync(
            "INSERT OR REPLACE INTO status_cache (key, value, updated_at) VALUES (@Key, @Value, @UpdatedAt)",
            new { Key = key, Value = value, UpdatedAt = DateTime.UtcNow.ToString("o") });
    }

    /// <summary>
    /// 读取缓存项
    /// </summary>
    public async Task<string?> GetAsync(string key)
    {
        using var db = CreateConnection();
        return await db.QuerySingleOrDefaultAsync<string?>(
            "SELECT value FROM status_cache WHERE key = @Key", new { Key = key });
    }

    /// <summary>
    /// 清除所有缓存
    /// </summary>
    public async Task ClearAsync()
    {
        using var db = CreateConnection();
        await db.ExecuteAsync("DELETE FROM status_cache");
    }
}
