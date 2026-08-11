using DotJob_Model.WebJobs;
using Host;
using Host.Common;
using Host.Common.Enums;
using Job_Scheduler.Application.Jobs;
using Microsoft.AspNetCore.Mvc;

namespace Job_Scheduler.Controllers;

/// <summary>
/// 测试控制器（用于批量压测数据构造）
/// </summary>
[ApiController]
[Route("api/test")]
public class TestController : ControllerBase
{
    private readonly SchedulerCenterServices _schedulerCenterServices;

    public TestController(SchedulerCenterServices schedulerCenterServices)
    {
        _schedulerCenterServices = schedulerCenterServices;
    }

    /// <summary>
    /// 批量生成定时任务，用于批量添加接口测试
    /// 触发类型随机为 Cron 或 Simple，间隔时间范围 10-60 秒，部分任务随机带执行次数限制
    /// </summary>
    /// <param name="count">生成任务数量，默认随机 100-200 个（范围建议 100-200）</param>
    [HttpGet("batch-create-jobs")]
    public async Task<object> BatchCreateJobsAsync([FromQuery] int? count = null)
    {
        var random = new Random();
        var total = count ?? random.Next(100, 201); // 默认 100-200 个
        total = Math.Clamp(total, 1, 1000);

        var groups = new[] { "测试分组A", "测试分组B", "测试分组C", "测试分组D" };

        // 常见 Cron 表达式，秒级触发，间隔覆盖 10-60 秒
        var cronExprs = new[]
        {
            "*/10 * * * * ?", // 每10秒
            "*/15 * * * * ?", // 每15秒
            "*/20 * * * * ?", // 每20秒
            "*/30 * * * * ?", // 每30秒
            "0 * * * * ?",    // 每分钟（60秒）
        };

        int success = 0, failed = 0;
        var failedList = new List<string>();
        var timestamp = DateTime.Now.ToString("HHmmss");

        for (int i = 1; i <= total; i++)
        {
            var jobName = $"测试任务_{timestamp}_{i:D3}_{random.Next(1000, 9999)}";
            var jobGroup = groups[random.Next(groups.Length)];

            // 随机决定触发类型：Cron 或 Simple
            var isCron = random.Next(2) == 0;

            string cronExpr = string.Empty;
            int? intervalSec = null;
            TriggerTypeEnum triggerType;

            if (isCron)
            {
                cronExpr = cronExprs[random.Next(cronExprs.Length)];
                triggerType = TriggerTypeEnum.Cron;
            }
            else
            {
                // Simple 触发器，间隔时间 10-60 秒
                intervalSec = random.Next(10, 61);
                triggerType = TriggerTypeEnum.Simple;
            }

            // 30% 概率带执行次数限制（RunTotal），其余为默认无限循环
            int? runTotal = random.Next(100) < 30 ? random.Next(1, 51) : null;

            var input = new AddWebJobs
            {
                JobName           = jobName,
                JobGroup          = jobGroup,
                JobType           = JobTypeEnum.Url,
                TriggerType       = triggerType,
                IntervalSecond    = intervalSec,
                Cron              = cronExpr,
                RequestType       = RequestTypeEnum.Get,
                RequestUrl        = "http://scheduler-service-stage.zjxqai.com/api/stellar-reports/correct-chinese-report?guid=11111",
                Headers           = string.Empty,
                RequestParameters = string.Empty,
                Description       = $"批量测试任务 #{i}，{(isCron ? "cron:" + cronExpr : "间隔 " + intervalSec + "s")}" +
                                     (runTotal.HasValue ? $"，执行次数限制 {runTotal}" : "，无限循环"),
                BeginTime         = DateTimeOffset.Now,
                EndTime           = null,
                RunTotal          = runTotal,
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
            Total = total,
            Success = success,
            Failed = failed,
            FailedList = failedList
        };
    }
}
