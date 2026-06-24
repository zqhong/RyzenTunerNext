using Microsoft.Data.Sqlite;

namespace RyzenTunerNext.Core.Data;

/// <summary>
/// 数据库初始化：建表、设置 WAL 模式
/// </summary>
public static class DatabaseInitializer
{
    public static void Initialize(string connectionString)
    {
        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        // 启用 WAL 模式
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "PRAGMA journal_mode=WAL;";
            cmd.ExecuteNonQuery();
        }

        // 建表
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS settings (
                    key   TEXT PRIMARY KEY,
                    value TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS logs (
                    id         INTEGER PRIMARY KEY AUTOINCREMENT,
                    timestamp  TEXT NOT NULL,
                    level      TEXT NOT NULL,
                    source     TEXT NOT NULL,
                    message    TEXT NOT NULL,
                    detail     TEXT
                );

                CREATE TABLE IF NOT EXISTS profiler_results (
                    id            INTEGER PRIMARY KEY AUTOINCREMENT,
                    created_at    TEXT NOT NULL,
                    test_type     TEXT NOT NULL,
                    score         REAL NOT NULL,
                    fast_limit    INTEGER NOT NULL,
                    slow_limit    INTEGER NOT NULL,
                    tctl_temp     INTEGER NOT NULL,
                    avg_frequency REAL,
                    avg_power     REAL,
                    max_temp      REAL,
                    efficiency    REAL
                );

                CREATE INDEX IF NOT EXISTS idx_logs_timestamp ON logs(timestamp DESC);
                CREATE INDEX IF NOT EXISTS idx_logs_level ON logs(level);

                -- 状态缓存表 (GUI 重启后可恢复状态显示)
                CREATE TABLE IF NOT EXISTS status_cache (
                    key         TEXT PRIMARY KEY,
                    value       TEXT NOT NULL,
                    updated_at  TEXT NOT NULL
                );
                """;
            cmd.ExecuteNonQuery();
        }
    }
}
