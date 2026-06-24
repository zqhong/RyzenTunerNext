using Dapper;
using System.Data;
using Microsoft.Data.Sqlite;
using RyzenTunerNext.Core.Models;

namespace RyzenTunerNext.Core.Data;

/// <summary>
/// 能效分析结果数据访问 (profiler_results 表)
/// </summary>
public class ProfilerResultRepository
{
    private readonly string _connectionString;

    public ProfilerResultRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    private IDbConnection CreateConnection() => new SqliteConnection(_connectionString);

    public async Task InsertAsync(ProfilerResult result)
    {
        using var db = CreateConnection();
        await db.ExecuteAsync(
            """
            INSERT INTO profiler_results (created_at, test_type, score, fast_limit, slow_limit, tctl_temp, avg_frequency, avg_power, max_temp, efficiency)
            VALUES (@CreatedAt, @TestType, @Score, @FastLimit, @SlowLimit, @TctlTemp, @AvgFrequency, @AvgPower, @MaxTemp, @Efficiency)
            """,
            new
            {
                result.CreatedAt,
                result.TestType,
                result.Score,
                result.FastLimit,
                result.SlowLimit,
                result.TctlTemp,
                result.AvgFrequency,
                result.AvgPower,
                result.MaxTemp,
                result.Efficiency
            });
    }

    public async Task<IEnumerable<ProfilerResult>> GetAllAsync()
    {
        using var db = CreateConnection();
        return await db.QueryAsync<ProfilerResult>(
            "SELECT * FROM profiler_results ORDER BY created_at DESC");
    }

    public async Task<IEnumerable<ProfilerResult>> GetByTestTypeAsync(string testType)
    {
        using var db = CreateConnection();
        return await db.QueryAsync<ProfilerResult>(
            "SELECT * FROM profiler_results WHERE test_type = @TestType ORDER BY created_at DESC",
            new { TestType = testType });
    }

    public async Task ClearAsync()
    {
        using var db = CreateConnection();
        await db.ExecuteAsync("DELETE FROM profiler_results");
    }
}
