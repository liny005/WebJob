using MySqlConnector;
using System.Diagnostics;

namespace Job_Scheduler.Application.Monitor;

public class MonitorMetric
{
    public DateTime Timestamp { get; set; }
    public double CpuUsagePercent { get; set; }
    public long MemoryMb { get; set; }
    public int WorkerThreadCount { get; set; }
    public long AverageLatencyMs { get; set; }
}

public class MonitorSnapshot
{
    public DateTime Timestamp { get; set; }
    public double CpuUsagePercent { get; set; }
    public long TotalManagedMemoryMb { get; set; }
    public long WorkingSetMb { get; set; }
    public long HeapSizeMb { get; set; }
    public int Gen2CollectionCount { get; set; }
    public int WorkerThreadCount { get; set; }
    public long AverageLatencyMs { get; set; }
    public long MaxLatencyMs { get; set; }
    public long MinLatencyMs { get; set; }
    public int TotalExecutions { get; set; }
}

public class MonitorService : BackgroundService
{
    private static readonly Queue<MonitorMetric> _history = new();
    private static MonitorSnapshot? _latest;
    private static readonly object _lock = new();

    private static readonly int MaxHistorySize = 360; // 3小时，每30秒一条

    private DateTime _lastCheckTime = DateTime.Now;
    private TimeSpan _lastCpuTime = Process.GetCurrentProcess().TotalProcessorTime;

    public static MonitorSnapshot? Latest
    {
        get { lock (_lock) { return _latest; } }
    }

    public static List<MonitorMetric> GetHistory(int minutes)
    {
        lock (_lock)
        {
            var cutoff = DateTime.Now.AddMinutes(-minutes);
            return _history.Where(m => m.Timestamp >= cutoff).OrderBy(m => m.Timestamp).ToList();
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 启动后稍等片刻再采集第一次，避免与应用启动竞争
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CollectAsync();
            }
            catch
            {
                // 采集失败不影响服务运行
            }

            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }

    private async Task CollectAsync()
    {
        var process = Process.GetCurrentProcess();

        // CPU 使用率（基于与上次的时间差）
        var now = DateTime.Now;
        var currentCpuTime = process.TotalProcessorTime;
        var cpuDelta = currentCpuTime - _lastCpuTime;
        var timeDelta = now - _lastCheckTime;
        var cpuPercent = timeDelta.TotalMilliseconds > 0
            ? Math.Round((cpuDelta.TotalMilliseconds / (Environment.ProcessorCount * timeDelta.TotalMilliseconds)) * 100, 2)
            : 0;
        _lastCheckTime = now;
        _lastCpuTime = currentCpuTime;

        // 内存
        var managedMb = GC.GetTotalMemory(false) / (1024 * 1024);
        var workingSetMb = process.WorkingSet64 / (1024 * 1024);

        // 线程池
        ThreadPool.GetAvailableThreads(out int availableWorker, out _);
        ThreadPool.GetMaxThreads(out int maxWorker, out _);
        var workerCount = maxWorker - availableWorker;

        // 任务延迟（数据库查询，这里是唯一的 DB 查询点）
        var (avgMs, maxMs, minMs, total) = await QueryLatencyAsync();

        var snapshot = new MonitorSnapshot
        {
            Timestamp = now,
            CpuUsagePercent = cpuPercent,
            TotalManagedMemoryMb = managedMb,
            WorkingSetMb = workingSetMb,
            HeapSizeMb = GC.GetTotalMemory(false) / (1024 * 1024),
            Gen2CollectionCount = GC.CollectionCount(2),
            WorkerThreadCount = workerCount,
            AverageLatencyMs = avgMs,
            MaxLatencyMs = maxMs,
            MinLatencyMs = minMs,
            TotalExecutions = total,
        };

        var metric = new MonitorMetric
        {
            Timestamp = now,
            CpuUsagePercent = cpuPercent,
            MemoryMb = workingSetMb,
            WorkerThreadCount = workerCount,
            AverageLatencyMs = avgMs,
        };

        lock (_lock)
        {
            _latest = snapshot;
            _history.Enqueue(metric);
            while (_history.Count > MaxHistorySize)
                _history.Dequeue();
        }
    }

    private static async Task<(long avg, long max, long min, int total)> QueryLatencyAsync()
    {
        await using var conn = new MySqlConnection(AppConfig.ConnectionString);
        await conn.OpenAsync();

        // 查询总执行次数
        await using var countCmd = conn.CreateCommand();
        countCmd.CommandText = "SELECT COUNT(*) FROM JOB_LOG WHERE EXECUTE_TIME > 0";
        var total = Convert.ToInt32(await countCmd.ExecuteScalarAsync());

        // 取最近1000条计算延迟统计
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT EXECUTE_TIME FROM JOB_LOG
                            WHERE EXECUTE_TIME > 0
                            ORDER BY BEGIN_TIME DESC
                            LIMIT 1000";
        await using var reader = await cmd.ExecuteReaderAsync();

        var latencies = new List<long>();
        while (await reader.ReadAsync())
        {
            var ms = (long)(reader.GetDouble(0) * 1000);
            if (ms > 0) latencies.Add(ms);
        }

        if (latencies.Count == 0) return (0, 0, 0, total);
        return ((long)Math.Round(latencies.Average()), latencies.Max(), latencies.Min(), total);
    }
}
