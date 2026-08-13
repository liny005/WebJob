using Job_Scheduler.Application.Jobs;
using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using System.Diagnostics;

namespace Job_Scheduler.Controllers;

/// <summary>
/// 监控数据历史记录
/// </summary>
public class MonitorMetric
{
    public DateTime Timestamp { get; set; }
    public double CpuUsagePercent { get; set; }
    public long MemoryMb { get; set; }
    public int WorkerThreadCount { get; set; }
    public long AverageLatencyMs { get; set; }
}

/// <summary>
/// 系统监控控制器
/// </summary>
[ApiController]
[Route("api/monitor")]
public class MonitorController : ControllerBase
{
    private readonly SchedulerCenterServices _schedulerCenterServices;
    private static DateTime _lastCheckTime = DateTime.UtcNow;
    private static TimeSpan _lastCpuTime = Process.GetCurrentProcess().TotalProcessorTime;
    private static readonly Queue<MonitorMetric> _metricsHistory = new();
    private static readonly int MaxHistorySize = 360; // 保留6小时的数据（1分钟采集一次）

    public MonitorController(SchedulerCenterServices schedulerCenterServices)
    {
        _schedulerCenterServices = schedulerCenterServices;
    }

    /// <summary>
    /// 获取系统监控数据
    /// </summary>
    [HttpGet("system")]
    public async Task<object> GetSystemMetricsAsync()
    {
        var process = Process.GetCurrentProcess();
        var threadCount = ProcessThreadCount.Current;

        var totalMemory = GC.GetTotalMemory(false);
        var managedMemory = totalMemory / (1024 * 1024); // MB
        var workingSet = process.WorkingSet64 / (1024 * 1024); // MB

        // 计算 CPU 使用率
        var currentTime = DateTime.UtcNow;
        var currentCpuTime = process.TotalProcessorTime;
        var cpuTimeDelta = currentCpuTime - _lastCpuTime;
        var timeDelta = currentTime - _lastCheckTime;

        var cpuUsagePercent = timeDelta.TotalMilliseconds > 0
            ? Math.Round((cpuTimeDelta.TotalMilliseconds / (Environment.ProcessorCount * timeDelta.TotalMilliseconds)) * 100, 2)
            : 0;

        _lastCheckTime = currentTime;
        _lastCpuTime = currentCpuTime;

        return new
        {
            Timestamp = DateTime.UtcNow,
            Cpu = new
            {
                UsagePercent = cpuUsagePercent
            },
            Memory = new
            {
                TotalManagedMemoryMb = managedMemory,
                WorkingSetMb = workingSet,
                HeapSizeMb = GC.GetTotalMemory(false) / (1024 * 1024),
                Gen2CollectionCount = GC.CollectionCount(2)
            },
            ThreadPool = new
            {
                WorkerThreadCount = threadCount.WorkerThreadCount
            }
        };
    }

    /// <summary>
    /// 获取任务执行延迟统计
    /// </summary>
    [HttpGet("job-latency")]
    public async Task<object> GetJobLatencyAsync()
    {
        await using var conn = new MySqlConnection(AppConfig.ConnectionString);
        await conn.OpenAsync();

        // 直接查询最近的日志记录，计算延迟统计
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT EXECUTE_TIME FROM JOB_LOG
                            WHERE EXECUTE_TIME > 0
                            ORDER BY BEGIN_TIME DESC
                            LIMIT 1000";

        await using var reader = await cmd.ExecuteReaderAsync();
        var latencies = new List<long>();

        while (await reader.ReadAsync())
        {
            var executeTime = reader.GetDouble(0);
            var durationMs = (long)(executeTime * 1000);
            if (durationMs > 0)
            {
                latencies.Add(durationMs);
            }
        }

        return new
        {
            AverageLatencyMs = latencies.Count > 0 ? (long)Math.Round(latencies.Average()) : 0L,
            MaxLatencyMs = latencies.Count > 0 ? latencies.Max() : 0L,
            MinLatencyMs = latencies.Count > 0 ? latencies.Min() : 0L,
            TotalExecutions = latencies.Count
        };
    }

    /// <summary>
    /// 获取完整的监控仪表板数据
    /// </summary>
    [HttpGet("dashboard")]
    public async Task<object> GetDashboardAsync()
    {
        var systemMetrics = await GetSystemMetricsAsync();
        var jobLatency = await GetJobLatencyAsync();

        var metric = new MonitorMetric
        {
            Timestamp = DateTime.UtcNow,
            CpuUsagePercent = double.Parse(systemMetrics.GetType().GetProperty("Cpu").GetValue(systemMetrics).GetType().GetProperty("UsagePercent").GetValue(systemMetrics.GetType().GetProperty("Cpu").GetValue(systemMetrics)).ToString()),
            MemoryMb = int.Parse(systemMetrics.GetType().GetProperty("Memory").GetValue(systemMetrics).GetType().GetProperty("WorkingSetMb").GetValue(systemMetrics.GetType().GetProperty("Memory").GetValue(systemMetrics)).ToString()),
            WorkerThreadCount = int.Parse(systemMetrics.GetType().GetProperty("ThreadPool").GetValue(systemMetrics).GetType().GetProperty("WorkerThreadCount").GetValue(systemMetrics.GetType().GetProperty("ThreadPool").GetValue(systemMetrics)).ToString()),
            AverageLatencyMs = long.Parse(jobLatency.GetType().GetProperty("AverageLatencyMs").GetValue(jobLatency).ToString())
        };

        lock (_metricsHistory)
        {
            _metricsHistory.Enqueue(metric);
            while (_metricsHistory.Count > MaxHistorySize)
                _metricsHistory.Dequeue();
        }

        return new
        {
            System = systemMetrics,
            JobLatency = jobLatency,
            Timestamp = DateTime.UtcNow
        };
    }

    /// <summary>
    /// 获取历史监控数据
    /// </summary>
    [HttpGet("history")]
    public IActionResult GetHistoryAsync([FromQuery] int minutes = 60)
    {
        lock (_metricsHistory)
        {
            var cutoffTime = DateTime.UtcNow.AddMinutes(-minutes);
            var history = _metricsHistory
                .Where(m => m.Timestamp >= cutoffTime)
                .OrderBy(m => m.Timestamp)
                .ToList();

            return Ok(new
            {
                Data = history,
                Count = history.Count,
                TimeRange = $"过去{minutes}分钟"
            });
        }
    }
}

/// <summary>
/// 线程池状态信息
/// </summary>
internal class ProcessThreadCount
{
    public int WorkerThreadCount { get; set; }
    public int IOThreadCount { get; set; }
    public long PendingWorkItemCount { get; set; }

    public static ProcessThreadCount Current
    {
        get
        {
            ThreadPool.GetAvailableThreads(out int workerThreads, out int ioThreads);
            ThreadPool.GetMaxThreads(out int maxWorkerThreads, out int maxIOThreads);

            return new ProcessThreadCount
            {
                WorkerThreadCount = maxWorkerThreads - workerThreads,
                IOThreadCount = maxIOThreads - ioThreads,
                PendingWorkItemCount = ThreadPool.PendingWorkItemCount
            };
        }
    }
}


