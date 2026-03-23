using DotJob_Model.WebJobs;
using Host;
using Host.Common;

namespace DotJob_Tests.Infrastructure;

/// <summary>
/// 快速构建测试用 AddWebJobs 输入对象
/// </summary>
public static class JobInputBuilder
{
    /// <summary>
    /// 构建一个简单间隔触发的任务输入（默认 60 秒间隔，无结束时间，无执行次数限制）
    /// </summary>
    public static AddWebJobs SimpleInterval(
        string name            = "TestJob",
        string group           = "TestGroup",
        int intervalSeconds    = 60,
        int? runTotal          = null,
        DateTimeOffset? endTime = null,
        string requestUrl      = "http://test.example.com/api")
        => new()
        {
            JobName        = name,
            JobGroup       = group,
            TriggerType    = TriggerTypeEnum.Simple,
            IntervalSecond = intervalSeconds,
            BeginTime      = DateTimeOffset.UtcNow,
            EndTime        = endTime,
            RunTotal       = runTotal,
            RequestUrl     = requestUrl,
            RequestType    = RequestTypeEnum.Get,
            Description    = $"Test job {name}",
        };

    /// <summary>
    /// 构建一个 Cron 触发的任务输入
    /// </summary>
    public static AddWebJobs Cron(
        string name         = "CronJob",
        string group        = "TestGroup",
        string cron         = "0/5 * * * * ?",
        string requestUrl   = "http://test.example.com/api")
        => new()
        {
            JobName     = name,
            JobGroup    = group,
            TriggerType = TriggerTypeEnum.Cron,
            Cron        = cron,
            BeginTime   = DateTimeOffset.UtcNow,
            RequestUrl  = requestUrl,
            RequestType = RequestTypeEnum.Get,
            Description = $"Cron job {name}",
        };
}
