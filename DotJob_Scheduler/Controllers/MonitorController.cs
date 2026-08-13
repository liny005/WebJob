using Job_Scheduler.Application.Jobs;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;

namespace Job_Scheduler.Controllers;

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
                UsagePercent = cpuUsagePercent,
                ProcessorCount = Environment.ProcessorCount
            },
            Memory = new
            {
                TotalManagedMemoryMb = managedMemory,
                WorkingSetMb = workingSet,
                HeapSizeMb = GC.GetTotalMemory(false) / (1024 * 1024),
                Gen0CollectionCount = GC.CollectionCount(0),
                Gen1CollectionCount = GC.CollectionCount(1),
                Gen2CollectionCount = GC.CollectionCount(2)
            },
            ThreadPool = new
            {
                WorkerThreadCount = threadCount.WorkerThreadCount,
                IOThreadCount = threadCount.IOThreadCount,
                PendingWorkItemCount = threadCount.PendingWorkItemCount
            }
        };
    }

    /// <summary>
    /// 获取任务执行延迟统计
    /// </summary>
    [HttpGet("job-latency")]
    public async Task<object> GetJobLatencyAsync()
    {
        var jobs = await _schedulerCenterServices.QueryAllJobsAsync();

        if (jobs.Count == 0)
        {
            return new
            {
                TotalJobs = 0,
                AverageLatencyMs = 0,
                MaxLatencyMs = 0,
                MinLatencyMs = 0,
                TotalExecutions = 0,
                LatencyByState = new object[]{ }
            };
        }

        var latencies = new List<long>();
        foreach (var job in jobs)
        {
            try
            {
                var jobLogs = await _schedulerCenterServices.QueryJobLogsAsync(job.Name, job.GroupName, 1, 100);
                foreach (var log in jobLogs.Data)
                {
                    // ExecuteTime 是以秒为单位，转换为毫秒
                    var durationMs = (long)(log.ExecuteTime * 1000);
                    if (durationMs > 0)
                    {
                        latencies.Add(durationMs);
                    }
                }
            }
            catch { }
        }

        return new
        {
            TotalJobs = jobs.Count,
            ExecutedJobs = latencies.Count > 0 ? Math.Min(jobs.Count, 100) : 0,
            AverageLatencyMs = latencies.Count > 0 ? (long)Math.Round(latencies.Average()) : 0L,
            MaxLatencyMs = latencies.Count > 0 ? latencies.Max() : 0L,
            MinLatencyMs = latencies.Count > 0 ? latencies.Min() : 0L,
            TotalExecutions = latencies.Count,
            LatencyByState = new
            {
                Normal = jobs.Count(j => j.TriggerState == 1),
                Paused = jobs.Count(j => j.TriggerState == 2),
                Blocked = jobs.Count(j => j.TriggerState == 5)
            }
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
        var jobStats = await _schedulerCenterServices.QueryAllJobsAsync();

        return new
        {
            System = systemMetrics,
            JobLatency = jobLatency,
            JobStats = new
            {
                Total = jobStats.Count,
                Normal = jobStats.Count(j => j.TriggerState == 1),
                Paused = jobStats.Count(j => j.TriggerState == 2),
                Blocked = jobStats.Count(j => j.TriggerState == 5)
            },
            Timestamp = DateTime.UtcNow
        };
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

