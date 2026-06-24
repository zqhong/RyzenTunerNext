using System.Diagnostics;

namespace RyzenTunerNext.Core.Services;

/// <summary>
/// 自写 C# 基准测试引擎（零外部依赖）。
/// 多线程素数计数（Eratosthenes 筛法变体），纯 CPU 计算密集型。
/// </summary>
public static class BenchmarkEngine
{
    /// <summary>
    /// 多核跑分: 统计 [2, upperBound] 范围内的素数个数。
    /// Parallel.For 占满所有逻辑核心。
    /// 返回素数个数和耗时(ms)。
    /// </summary>
    public static (long primeCount, double elapsedMs) RunMultiCore(long upperBound = 5_000_000)
    {
        var sw = Stopwatch.StartNew();
        long count = 0;

        int coreCount = Environment.ProcessorCount;
        long chunkSize = upperBound / coreCount;

        Parallel.For(0, coreCount, i =>
        {
            long start = i * chunkSize + 2;
            long end = (i == coreCount - 1) ? upperBound : (i + 1) * chunkSize + 1;
            long localCount = CountPrimesInRange(start, end);
            Interlocked.Add(ref count, localCount);
        });

        sw.Stop();
        return (count, sw.Elapsed.TotalMilliseconds);
    }

    /// <summary>
    /// 单核跑分
    /// </summary>
    public static (long primeCount, double elapsedMs) RunSingleCore(long upperBound = 1_000_000)
    {
        var sw = Stopwatch.StartNew();
        long count = CountPrimesInRange(2, upperBound);
        sw.Stop();
        return (count, sw.Elapsed.TotalMilliseconds);
    }

    private static long CountPrimesInRange(long start, long end)
    {
        long count = 0;
        for (long n = start; n <= end; n++)
        {
            if (IsPrime(n)) count++;
        }
        return count;
    }

    private static bool IsPrime(long n)
    {
        if (n < 2) return false;
        if (n == 2 || n == 3) return true;
        if (n % 2 == 0 || n % 3 == 0) return false;

        for (long i = 5; i * i <= n; i += 6)
        {
            if (n % i == 0 || n % (i + 2) == 0) return false;
        }
        return true;
    }
}
