using Dapper;
using System.Data;
using Microsoft.Data.Sqlite;
using RyzenTunerNext.Core.Models;

namespace RyzenTunerNext.Core.Data;

/// <summary>
/// 运行日志数据访问 (logs 表)
/// </summary>
public class LogRepository
{
    private readonly string _connectionString;

    public LogRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    private IDbConnection CreateConnection() => new SqliteConnection(_connectionString);

    public async Task InsertAsync(string level, string source, string message, string? detail = null)
    {
        using var db = CreateConnection();
        await db.ExecuteAsync(
            """
            INSERT INTO logs (timestamp, level, source, message, detail)
            VALUES (@Timestamp, @Level, @Source, @Message, @Detail)
            """,
            new
            {
                Timestamp = DateTime.UtcNow.ToString("o"),
                Level = level,
                Source = source,
                Message = message,
                Detail = detail
            });
    }

    public async Task InfoAsync(string source, string message, string? detail = null)
        => await InsertAsync("Info", source, message, detail);

    public async Task WarningAsync(string source, string message, string? detail = null)
        => await InsertAsync("Warning", source, message, detail);

    public async Task ErrorAsync(string source, string message, string? detail = null)
        => await InsertAsync("Error", source, message, detail);

    public async Task<IEnumerable<LogEntry>> QueryAsync(
        string? level = null, string? search = null, int limit = 200, int offset = 0)
    {
        using var db = CreateConnection();

        var sql = "SELECT id, timestamp, level, source, message, detail FROM logs WHERE 1=1";
        var parameters = new DynamicParameters();

        if (!string.IsNullOrEmpty(level))
        {
            sql += " AND level = @Level";
            parameters.Add("Level", level);
        }

        if (!string.IsNullOrEmpty(search))
        {
            sql += " AND (message LIKE @Search OR detail LIKE @Search)";
            parameters.Add("Search", $"%{search}%");
        }

        sql += " ORDER BY timestamp DESC LIMIT @Limit OFFSET @Offset";
        parameters.Add("Limit", limit);
        parameters.Add("Offset", offset);

        return await db.QueryAsync<LogEntry>(sql, parameters);
    }

    public async Task CleanupOlderThanAsync(int days)
    {
        using var db = CreateConnection();
        var cutoff = DateTime.UtcNow.AddDays(-days).ToString("o");
        await db.ExecuteAsync(
            "DELETE FROM logs WHERE timestamp < @Cutoff",
            new { Cutoff = cutoff });
    }
}
