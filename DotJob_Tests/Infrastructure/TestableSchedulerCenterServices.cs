using DotJob_Model;
using DotJob_Model.Entity;
using DotJob_Model.WebJobs;
using Job_Scheduler.Application.Jobs;
using Quartz;
using Quartz.Impl;
using Quartz.Impl.Matchers;
using Quartz.Spi;
using System.Collections.Specialized;

namespace DotJob_Tests.Infrastructure;

/// <summary>
/// 可测试的调度服务子类：
/// 1. 覆盖 GetSchedulerAsync()，注入纯内存 RAMJobStore 调度器，绕开真实 Quartz DB。
/// 2. 覆盖所有 MySQL 相关方法（UpsertJobConfig、LoadJobConfig、CountLogs、
///    GetJobEndTime、DeleteJobData、QueryJob），改用内存字典模拟，完全隔离数据库。
/// 每个测试实例拥有独立的调度器与内存存储，互不干扰。
/// </summary>
public class TestableSchedulerCenterServices : SchedulerCenterServices
{
    // ── 静态计数器，防止测试并发时调度器名称冲突 ─────────────────
    private static int _counter;

    // ── 内存调度器 ────────────────────────────────────────────────
    private IScheduler? _testScheduler;

    // ── 内存存储：模拟 JOB_CONFIG 表 ─────────────────────────────
    // key = "group::name"
    private readonly Dictionary<string, JobConfig> _configStore = new();

    // ── 内存存储：模拟 JOB_LOG 执行次数 ──────────────────────────
    // key = "group.name"（与生产代码一致）
    private readonly Dictionary<string, int> _logCountStore = new();

    // ── 结束时间注入（用于测试 ResumeJob 结束时间过期场景）────────
    // key = "group::name"，value = 结束时间（null 表示无限制）
    private readonly Dictionary<string, DateTime?> _endTimeOverride = new();

    // ── 构造函数 ──────────────────────────────────────────────────
    private TestableSchedulerCenterServices(ISchedulerFactory factory)
        : base(factory) { }

    // ── 工厂方法 ──────────────────────────────────────────────────

    /// <summary>
    /// 创建实例并启动内存调度器（每次调用生成唯一名称，防止测试间干扰）
    /// </summary>
    public static async Task<TestableSchedulerCenterServices> CreateAsync()
    {
        var name = $"TestScheduler_{Interlocked.Increment(ref _counter)}_{Guid.NewGuid():N}";
        var props = new NameValueCollection
        {
            ["quartz.scheduler.instanceName"]    = name,
            ["quartz.scheduler.instanceId"]      = "AUTO",
            ["quartz.jobStore.type"]             = "Quartz.Simpl.RAMJobStore, Quartz",
            ["quartz.threadPool.type"]           = "Quartz.Simpl.DefaultThreadPool, Quartz",
            ["quartz.threadPool.maxConcurrency"] = "10",
        };

        var factory = new StdSchedulerFactory(props);
        var svc = new TestableSchedulerCenterServices(factory);
        svc._testScheduler = await factory.GetScheduler();
        svc._testScheduler.JobFactory = new SimpleJobFactory();
        await svc._testScheduler.Start();
        return svc;
    }

    // ── 内存调度器覆盖 ────────────────────────────────────────────

    /// <summary>覆盖：直接返回内存调度器，不走 Quartz DB</summary>
    protected override Task<IScheduler> GetSchedulerAsync()
        => Task.FromResult(_testScheduler!);

    // ── 测试辅助方法 ──────────────────────────────────────────────

    /// <summary>
    /// 为指定任务注入结束时间（用于 ResumeJob 过期测试），
    /// 调用后 GetJobEndTimeAsync 将返回该时间而非查询数据库。
    /// </summary>
    public void SetEndTimeOverride(string jobGroup, string jobName, DateTime? endTime)
        => _endTimeOverride[$"{jobGroup}::{jobName}"] = endTime;

    /// <summary>
    /// 在内存日志计数中手动增加执行次数（模拟任务已执行 N 次）
    /// </summary>
    public void SetLogCount(string jobGroup, string jobName, int count)
        => _logCountStore[$"{jobGroup}.{jobName}"] = count;

    // ── MySQL 方法覆盖（全部改为内存操作）────────────────────────

    /// <summary>覆盖：写入内存字典，不操作 MySQL</summary>
    protected override Task UpsertJobConfigAsync(AddWebJobs input)
    {
        var key = $"{input.JobGroup}::{input.JobName}";

        // 如果已存在则保留 RunTotal（除非本次显式传了值）
        _configStore.TryGetValue(key, out var existing);

        _configStore[key] = new JobConfig
        {
            JobName           = input.JobName,
            JobGroup          = input.JobGroup,
            Description       = input.Description,
            RequestUrl        = input.RequestUrl ?? "",
            RequestType       = (int)input.RequestType,
            Headers           = input.Headers,
            RequestParameters = input.RequestParameters,
            TriggerType       = (int)input.TriggerType,
            Cron              = input.Cron,
            IntervalSecond    = input.IntervalSecond,
            BeginTime         = input.BeginTime.LocalDateTime,
            EndTime           = input.EndTime?.LocalDateTime,
            // 未传 RunTotal 时继承旧值
            RunTotal          = input.RunTotal ?? existing?.RunTotal,
            Dingtalk          = input.Dingtalk,
            MailMessage       = input.MailMessage,
        };
        return Task.CompletedTask;
    }

    /// <summary>覆盖：从内存字典读取，不查 MySQL</summary>
    protected override Task<JobConfig?> LoadJobConfigAsync(string jobGroup, string jobName)
    {
        _configStore.TryGetValue($"{jobGroup}::{jobName}", out var cfg);
        return Task.FromResult(cfg);
    }

    /// <summary>覆盖：从内存计数读取，不查 MySQL</summary>
    protected override Task<int> CountLogsAsync(string fullJobName)
    {
        _logCountStore.TryGetValue(fullJobName, out var count);
        return Task.FromResult(count);
    }

    /// <summary>
    /// 覆盖：优先使用测试注入的结束时间，其次使用内存配置中的值，不查 MySQL
    /// </summary>
    protected override Task<DateTime?> GetJobEndTimeAsync(string jobName, string jobGroup)
    {
        var overrideKey = $"{jobGroup}::{jobName}";
        if (_endTimeOverride.TryGetValue(overrideKey, out var overrideTime))
            return Task.FromResult(overrideTime);

        _configStore.TryGetValue(overrideKey, out var cfg);
        return Task.FromResult(cfg?.EndTime);
    }

    /// <summary>覆盖：从内存字典删除相关记录，不操作 MySQL</summary>
    protected override Task DeleteJobDataAsync(string jobGroup, string jobName)
    {
        var key = $"{jobGroup}::{jobName}";
        _configStore.Remove(key);
        _logCountStore.Remove($"{jobGroup}.{jobName}");
        return Task.CompletedTask;
    }

    /// <summary>
    /// 覆盖：直接从内存调度器读取全量任务列表，支持按名称/分组模糊筛选，
    /// 不依赖 MySQL QRTZ_TRIGGERS 查询。返回分页结果。
    /// </summary>
    public override async Task<PageResponse<JobListInfo>> QueryJobAsync(
        string? jobName = null, string? jobGroup = null,
        int pageNumber = 1, int pageSize = 20)
    {
        var all = await GetAllJobsInternalAsync(jobName, jobGroup);
        var total = all.Count;
        var paged = all.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToList();
        return new PageResponse<JobListInfo>
        {
            Data     = paged,
            PageInfo = new DotJob_Model.PageInfo { Total = total, PageSize = pageSize, PageNumber = pageNumber }
        };
    }

    /// <summary>
    /// 覆盖：返回全量任务列表（不分页），用于测试中的统计/断言。
    /// </summary>
    public override async Task<List<JobListInfo>> QueryAllJobsAsync(
        string? jobName = null, string? jobGroup = null)
    {
        return await GetAllJobsInternalAsync(jobName, jobGroup);
    }

    /// <summary>
    /// 内部：从内存调度器读取所有任务（不分页）
    /// </summary>
    private async Task<List<JobListInfo>> GetAllJobsInternalAsync(
        string? jobName = null, string? jobGroup = null)
    {
        var scheduler  = await GetSchedulerAsync();
        var groupNames = await scheduler.GetJobGroupNames();
        var result     = new List<JobListInfo>();

        foreach (var group in groupNames)
        {
            // 按分组模糊筛选
            if (!string.IsNullOrWhiteSpace(jobGroup) &&
                !group.Contains(jobGroup, StringComparison.OrdinalIgnoreCase))
                continue;

            var jobKeys = await scheduler.GetJobKeys(GroupMatcher<JobKey>.GroupEquals(group));
            foreach (var key in jobKeys)
            {
                // 按名称模糊筛选
                if (!string.IsNullOrWhiteSpace(jobName) &&
                    !key.Name.Contains(jobName, StringComparison.OrdinalIgnoreCase))
                    continue;

                var triggers = await scheduler.GetTriggersOfJob(key);
                var trigger  = triggers.FirstOrDefault();
                var state    = trigger != null
                    ? await scheduler.GetTriggerState(trigger.Key)
                    : TriggerState.None;

                var stateInt = state switch
                {
                    TriggerState.Normal   => 1,
                    TriggerState.Paused   => 2,
                    TriggerState.Complete => 3,
                    TriggerState.Error    => 4,
                    TriggerState.Blocked  => 5,
                    _                     => 0,
                };

                _configStore.TryGetValue($"{group}::{key.Name}", out var cfg);

                result.Add(new JobListInfo
                {
                    Name             = key.Name,
                    GroupName        = group,
                    TriggerState     = stateInt,
                    TriggerType      = cfg?.TriggerType ?? 0,
                    Cron             = cfg?.Cron,
                    IntervalSecond   = cfg?.IntervalSecond,
                    NextFireTime     = trigger?.GetNextFireTimeUtc()?.LocalDateTime,
                    PreviousFireTime = trigger?.GetPreviousFireTimeUtc()?.LocalDateTime,
                });
            }
        }

        return result;
    }
}

/// <summary>
/// 简单 Job 工厂：通过反射直接实例化，不依赖 DI 容器
/// </summary>
file sealed class SimpleJobFactory : IJobFactory
{
    public IJob NewJob(TriggerFiredBundle bundle, IScheduler scheduler)
        => (IJob)Activator.CreateInstance(bundle.JobDetail.JobType)!;

    public void ReturnJob(IJob job) { }
}
