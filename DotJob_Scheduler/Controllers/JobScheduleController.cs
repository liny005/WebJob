using DotJob_Model.WebJobs;
using DotJob_Model;
using Job_Scheduler.Application.Jobs;
using Job_Scheduler.Application.User;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Model;
using System.Text.Json;
using DotJob_Model.Entity;

namespace Job_Scheduler.Controllers;

/// <summary>
/// 任务调度控制器
/// </summary>
[ApiController]
[Route("api/job")]
public class JobScheduleController : ControllerBase
{
    private readonly SchedulerCenterServices _schedulerCenterServices;
    private readonly AuditLogService _auditLogService;

    public JobScheduleController(SchedulerCenterServices schedulerCenterServices, AuditLogService auditLogService)
    {
        _schedulerCenterServices = schedulerCenterServices;
        _auditLogService = auditLogService;
    }

    /// <summary>
    /// 查询任务列表（服务端分页）
    /// </summary>
    [HttpGet("list")]
    public async Task<PageResponse<JobListInfo>> QueryJobListAsync(
        [FromQuery] string? jobName = null,
        [FromQuery] string? jobGroup = null,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20)
    {
        return await _schedulerCenterServices.QueryJobAsync(jobName, jobGroup, pageNumber, pageSize);
    }

    /// <summary>
    /// 查询任务统计（全量，不分页），用于首页统计卡片
    /// </summary>
    [HttpGet("stats")]
    public async Task<object> QueryJobStatsAsync()
    {
        var all = await _schedulerCenterServices.QueryAllJobsAsync();
        return new
        {
            Total   = all.Count,
            Normal  = all.Count(j => j.TriggerState == 1),
            Paused  = all.Count(j => j.TriggerState == 2),
            Blocked = all.Count(j => j.TriggerState == 5)
        };
    }

    /// <summary>
    /// 添加定时任务
    /// </summary>
    /// <param name="input">任务配置信息</param>
    /// <returns>返回添加结果</returns>
    [HttpPost("add"), Authorize]
    public async Task AddScheduleJobAsync([FromBody] AddWebJobs input)
    {
        // 调用服务添加任务
        await _schedulerCenterServices.AddScheduleJobAsync(input);
        await RecordAudit("新增任务", $"{input.JobGroup}.{input.JobName}");
    }

    /// <summary>
    /// 立即执行一次任务（不影响原有调度计划）
    /// </summary>
    [HttpPost("trigger"), Authorize]
    public async Task TriggerJobNowAsync([FromQuery] string jobName, [FromQuery] string jobGroup)
    {
        await _schedulerCenterServices.TriggerJobNowAsync(jobGroup, jobName);
        await RecordAudit("立即执行", $"{jobGroup}.{jobName}");
    }

    /// <summary>
    /// 暂停任务
    /// </summary>
    [HttpPost("pause"), Authorize]
    public async Task PauseJobAsync([FromQuery] string jobName, [FromQuery] string jobGroup)
    {
        await _schedulerCenterServices.PauseJonAsync(jobGroup, jobName);
        await RecordAudit("暂停任务", $"{jobGroup}.{jobName}");
    }

    /// <summary>
    /// 恢复任务
    /// </summary>
    [HttpPost("resume"), Authorize]
    public async Task ResumeJobAsync([FromQuery] string jobName, [FromQuery] string jobGroup)
    {
        await _schedulerCenterServices.ResumeJobAsync(jobGroup, jobName);
        await RecordAudit("恢复任务", $"{jobGroup}.{jobName}");
    }

    /// <summary>
    /// 修改定时任务（任务名称和分组不可修改）
    /// </summary>
    [HttpPut("update"), Authorize]
    public async Task UpdateScheduleJobAsync([FromBody] AddWebJobs input)
    {
        await _schedulerCenterServices.UpdateScheduleJobAsync(input);
        var remark = JsonSerializer.Serialize(input, new JsonSerializerOptions { WriteIndented = true });
        await RecordAudit("修改任务", $"{input.JobGroup}.{input.JobName}", remark);
    }

    /// <summary>
    /// 删除任务
    /// </summary>
    [HttpGet("delete"), Authorize]
    public async Task DeleteJobAsync([FromQuery] string jobName, [FromQuery] string jobGroup)
    {
        await _schedulerCenterServices.DelJobAsync(jobGroup, jobName);
        await RecordAudit("删除任务", $"{jobGroup}.{jobName}");
    }

    /// <summary>
    /// 查询指定任务的运行日志
    /// </summary>
    /// <param name="jobName">任务名称</param>
    /// <param name="jobGroup">任务分组</param>
    /// <param name="pageNumber">页码（从1开始）</param>
    /// <param name="pageSize">每页数量</param>
    /// <returns>分页的日志列表</returns>
    [HttpGet("logs")]
    public async Task<PageResponse<LogEntity>> GetJobLogsAsync(
        [FromQuery] string jobName,
        [FromQuery] string jobGroup,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20)
    {
        return await _schedulerCenterServices.QueryJobLogsAsync(jobName, jobGroup, pageNumber, pageSize);
    }

    /// <summary>
    /// 获取任务详情
    /// </summary>
    /// <param name="jobName">任务名称</param>
    /// <param name="jobGroup">任务分组</param>
    /// <returns>任务详情</returns>
    [HttpGet("detail")]
    public async Task<JobDetailInfo?> GetJobDetailAsync([FromQuery] string jobName, [FromQuery] string jobGroup)
    {
        return await _schedulerCenterServices.GetJobDetailAsync(jobGroup, jobName);
    }

    /// <summary>
    /// 批量添加压测任务（用于并发测试）
    /// </summary>
    /// <param name="count">任务数量，默认 200</param>
    /// <param name="intervalSecond">执行间隔（秒），默认 10</param>
    [HttpPost("batch-add-test")]
    public async Task<object> BatchAddTestJobsAsync(
        [FromQuery] int count = 10)
    {
        var random = new Random();
        var groups = new[] { "压测分组A", "压测分组B", "压测分组C", "压测分组D", "压测分组E" };

        int success = 0, failed = 0;
        var failedList = new List<string>();

        for (int i = 1; i <= count; i++)
        {
            var jobName  = $"压测任务_{i:D3}_{random.Next(1000, 9999)}";
            var jobGroup = groups[random.Next(groups.Length)];

            // 随机决定是 Cron 任务 还是 Simple 任务
            var isCron = random.Next(2) == 0; // 50% 概率

            string cronExpr = string.Empty;
            int? intervalSec = null;
            Host.Common.TriggerTypeEnum triggerType;

            if (isCron)
            {
                // 随机选择 1 分钟或 5 分钟的 cron 表达式
                var choose = random.Next(2);
                if (choose == 0)
                    cronExpr = "0 */1 * * * ?"; // 每分钟
                else
                    cronExpr = "0 */5 * * * ?"; // 每5分钟

                triggerType = Host.Common.TriggerTypeEnum.Cron;
            }
            else
            {
                // 简单触发器，随机间隔 10-120 秒
                intervalSec = random.Next(10, 121);
                triggerType = Host.Common.TriggerTypeEnum.Simple;
            }

            var input = new AddWebJobs
            {
                JobName           = jobName,
                JobGroup          = jobGroup,
                JobType           = Host.Common.Enums.JobTypeEnum.Url,
                TriggerType       = triggerType,
                IntervalSecond    = intervalSec,
                Cron              = cronExpr,
                RequestType       = Host.RequestTypeEnum.Get,
                RequestUrl        = "http://scheduler-service-stage.zjxqai.com/api/stellar-reports/correct-chinese-report?guid=11111",
                Headers           = string.Empty,
                RequestParameters = string.Empty,
                Description       = $"并发压测任务 #{i}，{(isCron ? "cron:" + cronExpr : "间隔 " + (intervalSec?.ToString() + "s"))}",
                BeginTime         = DateTimeOffset.Now,
                EndTime           = null,
                RunTotal          = null,
                MailMessage       = 0,
                Dingtalk          = 0,
                RunNumber         = 0
            };

            try
            {
                await _schedulerCenterServices.AddScheduleJobAsync(input);
                success++;
            }
            catch (Exception ex)
            {
                failed++;
                failedList.Add($"#{i} {jobGroup}.{jobName}: {ex.Message}");
            }
        }

        return new
        {
            Total   = count,
            Success = success,
            Failed  = failed,
            FailedList = failedList
        };
    }

    /// <summary>
    /// 批量删除所有任务（用于测试清理）
    /// </summary>
    [HttpGet("batch-delete-all")]
    public async Task<object> BatchDeleteAllJobsAsync()
    {
        var jobs = await _schedulerCenterServices.QueryAllJobsAsync();
        int success = 0, failed = 0;
        var failedList = new List<string>();

        foreach (var job in jobs)
        {
            try
            {
                await _schedulerCenterServices.DelJobAsync(job.GroupName, job.Name);
                success++;
            }
            catch (Exception ex)
            {
                failed++;
                failedList.Add($"{job.GroupName}.{job.Name}: {ex.Message}");
            }
        }

        return new
        {
            Total   = jobs.Count,
            Success = success,
            Failed  = failed,
            FailedList = failedList
        };
    }

    private async Task RecordAudit(string action, string target, string? remark = null)
    {
        var operatorName = User.Identity?.Name ?? "unknown";
        var displayName = User.FindFirst("DisplayName")?.Value;
        await _auditLogService.LogAsync(operatorName, displayName, action, target, remark);
    }
}