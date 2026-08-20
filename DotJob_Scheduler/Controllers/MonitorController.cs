using Job_Scheduler.Application.Monitor;
using Microsoft.AspNetCore.Mvc;

namespace Job_Scheduler.Controllers;

[ApiController]
[Route("api/monitor")]
public class MonitorController : ControllerBase
{
    /// <summary>
    /// 获取最新系统监控快照（来自后台定时采集，无DB查询）
    /// </summary>
    [HttpGet("dashboard")]
    public IActionResult GetDashboard()
    {
        var snapshot = MonitorService.Latest;
        if (snapshot == null)
            return Ok(new { message = "数据采集中，请稍后再试（服务启动10秒后开始采集）" });

        return Ok(new
        {
            System = new
            {
                Timestamp = snapshot.Timestamp,
                Cpu = new { UsagePercent = snapshot.CpuUsagePercent },
                Memory = new
                {
                    TotalManagedMemoryMb = snapshot.TotalManagedMemoryMb,
                    WorkingSetMb = snapshot.WorkingSetMb,
                    HeapSizeMb = snapshot.HeapSizeMb,
                    Gen2CollectionCount = snapshot.Gen2CollectionCount,
                },
                ThreadPool = new { WorkerThreadCount = snapshot.WorkerThreadCount },
            },
            JobLatency = new
            {
                AverageLatencyMs = snapshot.AverageLatencyMs,
                MaxLatencyMs = snapshot.MaxLatencyMs,
                MinLatencyMs = snapshot.MinLatencyMs,
                TotalExecutions = snapshot.TotalExecutions,
            },
            Timestamp = snapshot.Timestamp,
        });
    }

    /// <summary>
    /// 获取历史监控数据（内存，无DB查询）
    /// </summary>
    [HttpGet("history")]
    public IActionResult GetHistory([FromQuery] int minutes = 60)
    {
        var history = MonitorService.GetHistory(minutes);
        return Ok(new
        {
            Data = history,
            Count = history.Count,
            TimeRange = $"过去{minutes}分钟",
        });
    }
}
