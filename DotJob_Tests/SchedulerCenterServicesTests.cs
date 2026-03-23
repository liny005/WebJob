using DotJob_Tests.Infrastructure;
using FluentAssertions;
using Quartz;

namespace DotJob_Tests;

/// <summary>
/// SchedulerCenterServices 方法单元测试
///
/// 策略：TestableSchedulerCenterServices 继承自 SchedulerCenterServices，
/// 覆盖 GetSchedulerAsync() 注入纯内存 RAMJobStore 调度器，
/// 覆盖所有 MySQL 相关方法（UpsertJobConfig / LoadJobConfig / CountLogs /
/// GetJobEndTime / DeleteJobData / QueryJob），改用内存字典模拟。
/// 所有业务逻辑（参数校验、触发器构建、状态判断等）均执行真实代码，完全隔离数据库。
///
/// 每个测试方法独享一个调度器实例，互不干扰。
/// </summary>
public class SchedulerCenterServicesTests : IAsyncLifetime
{
    private TestableSchedulerCenterServices _svc = null!;
    private IScheduler _scheduler = null!;

    // ── 生命周期 ──────────────────────────────────────────────────

    public async Task InitializeAsync()
    {
        _svc = await TestableSchedulerCenterServices.CreateAsync();
        _scheduler = await _svc.GetSchedulerForInternalUseAsync();
    }

    public async Task DisposeAsync()
    {
        await _svc.ShutdownAsync(waitForJobsToComplete: false);
    }

    // ═══════════════════════════════════════════════════════════════
    // AddScheduleJobAsync
    // ═══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "AddJob - 添加成功后，任务应存在于调度器中")]
    public async Task AddScheduleJobAsync_Success_JobExistsInScheduler()
    {
        var input = JobInputBuilder.SimpleInterval("Job1", "G1");

        await _svc.AddScheduleJobAsync(input);

        var exists = await _scheduler.CheckExists(new JobKey("Job1", "G1"));
        exists.Should().BeTrue("任务应已注册到调度器");
    }

    [Fact(DisplayName = "AddJob - 添加成功后，GetJobDetailAsync 应能正确返回 RequestUrl 和 RunTotal")]
    public async Task AddScheduleJobAsync_Detail_StoredCorrectly()
    {
        var input = JobInputBuilder.SimpleInterval(
            "Job2", "G2",
            runTotal: 5,
            requestUrl: "http://example.com/test");

        await _svc.AddScheduleJobAsync(input);

        var detail = await _svc.GetJobDetailAsync("G2", "Job2");
        detail.Should().NotBeNull();
        detail!.RequestUrl.Should().Be("http://example.com/test");
        detail.RunTotal.Should().Be(5);
        detail.RunNumber.Should().Be(0, "新建任务执行次数应为 0");
    }

    [Fact(DisplayName = "AddJob - 添加 Simple 触发器，间隔时间应正确")]
    public async Task AddScheduleJobAsync_SimpleTrigger_IntervalCorrect()
    {
        var input = JobInputBuilder.SimpleInterval("Job3", "G3", intervalSeconds: 30);

        await _svc.AddScheduleJobAsync(input);

        var triggers = await _scheduler.GetTriggersOfJob(new JobKey("Job3", "G3"));
        var simple = triggers.First() as ISimpleTrigger;
        simple.Should().NotBeNull();
        simple!.RepeatInterval.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact(DisplayName = "AddJob - 添加 Cron 触发器，Cron 表达式应正确")]
    public async Task AddScheduleJobAsync_CronTrigger_CronExpressionCorrect()
    {
        var input = JobInputBuilder.Cron("CronJob1", "CronG", cron: "0 0/1 * * * ?");

        await _svc.AddScheduleJobAsync(input);

        var triggers = await _scheduler.GetTriggersOfJob(new JobKey("CronJob1", "CronG"));
        var cron = triggers.First() as ICronTrigger;
        cron.Should().NotBeNull();
        cron!.CronExpressionString.Should().Be("0 0/1 * * * ?");
    }

    [Fact(DisplayName = "AddJob - 重复添加同名任务，应抛出异常")]
    public async Task AddScheduleJobAsync_DuplicateKey_ThrowsException()
    {
        var input = JobInputBuilder.SimpleInterval("JobDup", "GDup");
        await _svc.AddScheduleJobAsync(input);

        var act = async () => await _svc.AddScheduleJobAsync(input);

        await act.Should().ThrowAsync<Exception>()
            .WithMessage("*already exists*", "重复添加应抛出包含 'already exists' 的异常");
    }

    [Fact(DisplayName = "AddJob - 分页查询应能查到刚添加的任务")]
    public async Task AddScheduleJobAsync_QueryJobAsync_ReturnsAddedJob()
    {
        var input = JobInputBuilder.SimpleInterval("JobQuery", "GQuery");
        await _svc.AddScheduleJobAsync(input);

        var result = await _svc.QueryAllJobsAsync();

        result.Should().Contain(j => j.Name == "JobQuery" && j.GroupName == "GQuery",
            "查询结果应包含刚添加的任务");
    }

    // ═══════════════════════════════════════════════════════════════
    // UpdateScheduleJobAsync
    // ═══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "UpdateJob - 修改间隔时间，新触发器间隔应生效")]
    public async Task UpdateScheduleJobAsync_IntervalChanged_TriggerUpdated()
    {
        var input = JobInputBuilder.SimpleInterval("JobUpd", "GUpd", intervalSeconds: 60);
        await _svc.AddScheduleJobAsync(input);

        // 修改间隔为 120 秒
        input.IntervalSecond = 120;
        await _svc.UpdateScheduleJobAsync(input);

        var triggers = await _scheduler.GetTriggersOfJob(new JobKey("JobUpd", "GUpd"));
        var simple = triggers.First() as ISimpleTrigger;
        simple!.RepeatInterval.Should().Be(TimeSpan.FromSeconds(120), "修改后间隔应为 120 秒");
    }

    [Fact(DisplayName = "UpdateJob - 修改 RequestUrl，GetJobDetailAsync 应返回新 URL")]
    public async Task UpdateScheduleJobAsync_RequestUrl_UpdatedInDetail()
    {
        var input = JobInputBuilder.SimpleInterval("JobUrl", "GUrl", requestUrl: "http://old.com");
        await _svc.AddScheduleJobAsync(input);

        input.RequestUrl = "http://new.com";
        await _svc.UpdateScheduleJobAsync(input);

        var detail = await _svc.GetJobDetailAsync("GUrl", "JobUrl");
        detail!.RequestUrl.Should().Be("http://new.com");
    }

    [Fact(DisplayName = "UpdateJob - 修改不应重置已执行次数 RunNumber")]
    public async Task UpdateScheduleJobAsync_RunNumber_Preserved()
    {
        var input = JobInputBuilder.SimpleInterval("JobRun", "GRun");
        await _svc.AddScheduleJobAsync(input);

        // 模拟任务已执行 3 次
        _svc.SetLogCount("GRun", "JobRun", 3);

        // 通过真实的 UpdateScheduleJobAsync 修改 URL
        input.RequestUrl = "http://updated.com";
        await _svc.UpdateScheduleJobAsync(input);

        var detail = await _svc.GetJobDetailAsync("GRun", "JobRun");
        detail!.RunNumber.Should().Be(3, "修改任务不应重置执行次数");
    }

    [Fact(DisplayName = "UpdateJob - 前端未传 RunTotal 时，应继承旧值不丢失")]
    public async Task UpdateScheduleJobAsync_RunTotal_InheritedWhenNotProvided()
    {
        var input = JobInputBuilder.SimpleInterval("JobRT", "GRT", runTotal: 10);
        await _svc.AddScheduleJobAsync(input);

        // 修改时不传 RunTotal（置为 null 模拟前端未填写）
        input.RunTotal = null;
        input.RequestUrl = "http://changed.com";
        await _svc.UpdateScheduleJobAsync(input);

        var detail = await _svc.GetJobDetailAsync("GRT", "JobRT");
        detail!.RunTotal.Should().Be(10, "未传 RunTotal 时应继承旧值 10");
    }

    [Fact(DisplayName = "UpdateJob - 修改前暂停的任务，修改后应保持暂停状态")]
    public async Task UpdateScheduleJobAsync_WasPaused_RemainsPaused()
    {
        var input = JobInputBuilder.SimpleInterval("JobPaused", "GPaused");
        await _svc.AddScheduleJobAsync(input);
        await _svc.PauseJonAsync("GPaused", "JobPaused");

        input.RequestUrl = "http://updated.com";
        await _svc.UpdateScheduleJobAsync(input);

        var triggers = await _scheduler.GetTriggersOfJob(new JobKey("JobPaused", "GPaused"));
        var state = await _scheduler.GetTriggerState(triggers.First().Key);
        state.Should().Be(TriggerState.Paused, "修改前已暂停，修改后应保持暂停");
    }

    [Fact(DisplayName = "UpdateJob - 修改不存在的任务，应抛出异常")]
    public async Task UpdateScheduleJobAsync_NotExist_ThrowsException()
    {
        var input = JobInputBuilder.SimpleInterval("NotExist", "GNotExist");

        var act = async () => await _svc.UpdateScheduleJobAsync(input);

        await act.Should().ThrowAsync<Exception>()
            .WithMessage("*does not exist*");
    }

    // ═══════════════════════════════════════════════════════════════
    // TriggerJobNowAsync
    // ═══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "TriggerJobNow - 任务存在时，不应抛出异常（触发成功）")]
    public async Task TriggerJobNowAsync_JobExists_NoException()
    {
        var input = JobInputBuilder.SimpleInterval("JobTrig", "GTrig");
        await _svc.AddScheduleJobAsync(input);

        var act = async () => await _svc.TriggerJobNowAsync("GTrig", "JobTrig");

        await act.Should().NotThrowAsync("任务存在，立即触发不应报错");
    }

    [Fact(DisplayName = "TriggerJobNow - 任务不存在时，应抛出含任务名的异常")]
    public async Task TriggerJobNowAsync_JobNotExist_ThrowsException()
    {
        var act = async () => await _svc.TriggerJobNowAsync("GGhost", "GhostJob");

        await act.Should().ThrowAsync<Exception>()
            .WithMessage("*GhostJob*", "异常信息应包含任务名称");
    }

    // ═══════════════════════════════════════════════════════════════
    // PauseJonAsync
    // ═══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "PauseJob - 暂停后触发器状态应为 Paused")]
    public async Task PauseJonAsync_TriggerState_ShouldBePaused()
    {
        var input = JobInputBuilder.SimpleInterval("JobPause", "GPause");
        await _svc.AddScheduleJobAsync(input);

        await _svc.PauseJonAsync("GPause", "JobPause");

        var triggers = await _scheduler.GetTriggersOfJob(new JobKey("JobPause", "GPause"));
        var state = await _scheduler.GetTriggerState(triggers.First().Key);
        state.Should().Be(TriggerState.Paused);
    }

    [Fact(DisplayName = "PauseJob - 暂停后查询列表，TriggerState 应为 2（Paused）")]
    public async Task PauseJonAsync_QueryJobAsync_ShowsPausedState()
    {
        var input = JobInputBuilder.SimpleInterval("JobPause2", "GPause2");
        await _svc.AddScheduleJobAsync(input);
        await _svc.PauseJonAsync("GPause2", "JobPause2");

        var result = await _svc.QueryAllJobsAsync();
        var job = result.FirstOrDefault(j => j.Name == "JobPause2");

        job.Should().NotBeNull();
        job!.TriggerState.Should().Be(2, "暂停后列表中 TriggerState 应为 2");
    }

    // ═══════════════════════════════════════════════════════════════
    // ResumeJobAsync
    // ═══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "ResumeJob - 暂停后恢复，触发器状态应变为 Normal")]
    public async Task ResumeJobAsync_AfterPause_TriggerStateNormal()
    {
        var input = JobInputBuilder.SimpleInterval("JobResume", "GResume");
        await _svc.AddScheduleJobAsync(input);
        await _svc.PauseJonAsync("GResume", "JobResume");

        await _svc.ResumeJobAsync("GResume", "JobResume");

        var triggers = await _scheduler.GetTriggersOfJob(new JobKey("JobResume", "GResume"));
        var state = await _scheduler.GetTriggerState(triggers.First().Key);
        state.Should().Be(TriggerState.Normal, "恢复后触发器应为 Normal");
    }

    [Fact(DisplayName = "ResumeJob - 结束时间已过期的任务，恢复应抛出异常")]
    public async Task ResumeJobAsync_EndTimeExpired_ThrowsException()
    {
        // 添加任务（不带结束时间，避免触发器创建失败）
        var input = JobInputBuilder.SimpleInterval("JobExpired", "GExpired");
        await _svc.AddScheduleJobAsync(input);
        await _svc.PauseJonAsync("GExpired", "JobExpired");

        // 通过测试辅助方法注入一个已过期的结束时间（不走数据库）
        _svc.SetEndTimeOverride("GExpired", "JobExpired", DateTime.Now.AddSeconds(-10));

        var act = async () => await _svc.ResumeJobAsync("GExpired", "JobExpired");

        await act.Should().ThrowAsync<Exception>()
            .WithMessage("*end time*", "结束时间已过期，不允许恢复");
    }

    // ═══════════════════════════════════════════════════════════════
    // DelJobAsync
    // ═══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "DelJob - 删除后，任务不应再存在于调度器")]
    public async Task DelJobAsync_JobRemoved_FromScheduler()
    {
        var input = JobInputBuilder.SimpleInterval("JobDel", "GDel");
        await _svc.AddScheduleJobAsync(input);

        await _svc.DelJobAsync("GDel", "JobDel");

        var exists = await _scheduler.CheckExists(new JobKey("JobDel", "GDel"));
        exists.Should().BeFalse("删除后任务不应存在");
    }

    [Fact(DisplayName = "DelJob - 删除后，分页查询不应再包含该任务")]
    public async Task DelJobAsync_QueryJobAsync_NotContainDeletedJob()
    {
        var input = JobInputBuilder.SimpleInterval("JobDel2", "GDel2");
        await _svc.AddScheduleJobAsync(input);
        await _svc.DelJobAsync("GDel2", "JobDel2");

        var result = await _svc.QueryAllJobsAsync();

        result.Should().NotContain(j => j.Name == "JobDel2",
            "删除后查询结果不应包含该任务");
    }

    [Fact(DisplayName = "DelJob - 删除后，GetJobDetailAsync 应返回 null")]
    public async Task DelJobAsync_GetJobDetail_ReturnsNull()
    {
        var input = JobInputBuilder.SimpleInterval("JobDel3", "GDel3");
        await _svc.AddScheduleJobAsync(input);
        await _svc.DelJobAsync("GDel3", "JobDel3");

        var detail = await _svc.GetJobDetailAsync("GDel3", "JobDel3");
        detail.Should().BeNull("删除后 GetJobDetailAsync 应返回 null");
    }

    // ═══════════════════════════════════════════════════════════════
    // GetJobDetailAsync
    // ═══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "GetJobDetail - 返回的字段应与添加时的输入一致")]
    public async Task GetJobDetailAsync_FieldsMatchInput()
    {
        var input = JobInputBuilder.SimpleInterval(
            "JobDetail", "GDetail",
            intervalSeconds: 45,
            runTotal: 7,
            requestUrl: "http://detail.test.com");
        await _svc.AddScheduleJobAsync(input);

        var detail = await _svc.GetJobDetailAsync("GDetail", "JobDetail");

        detail.Should().NotBeNull();
        detail!.JobName.Should().Be("JobDetail");
        detail.JobGroup.Should().Be("GDetail");
        detail.IntervalSecond.Should().Be(45);
        detail.RunTotal.Should().Be(7);
        detail.RequestUrl.Should().Be("http://detail.test.com");
        detail.TriggerType.Should().Be(2, "Simple 触发器类型为 2");
    }

    [Fact(DisplayName = "GetJobDetail - 不存在的任务应返回 null")]
    public async Task GetJobDetailAsync_NotExist_ReturnsNull()
    {
        var detail = await _svc.GetJobDetailAsync("GGhost", "GhostJob");

        detail.Should().BeNull("不存在的任务应返回 null");
    }

    [Fact(DisplayName = "GetJobDetail - RunNumber 初始值应为 0")]
    public async Task GetJobDetailAsync_RunNumber_InitiallyZero()
    {
        var input = JobInputBuilder.SimpleInterval("JobRunNum", "GRunNum");
        await _svc.AddScheduleJobAsync(input);

        var detail = await _svc.GetJobDetailAsync("GRunNum", "JobRunNum");

        detail!.RunNumber.Should().Be(0);
    }

    [Fact(DisplayName = "GetJobDetail - SetLogCount 模拟执行后，RunNumber 应正确反映")]
    public async Task GetJobDetailAsync_RunNumber_ReflectsLogCount()
    {
        var input = JobInputBuilder.SimpleInterval("JobRunCount", "GRunCount");
        await _svc.AddScheduleJobAsync(input);

        // 模拟任务执行了 5 次
        _svc.SetLogCount("GRunCount", "JobRunCount", 5);

        var detail = await _svc.GetJobDetailAsync("GRunCount", "JobRunCount");

        detail!.RunNumber.Should().Be(5, "RunNumber 应等于模拟的执行次数");
    }

    [Fact(DisplayName = "GetJobDetail - Cron 任务返回正确的 Cron 表达式和触发器类型")]
    public async Task GetJobDetailAsync_CronTrigger_ReturnsCorrectCron()
    {
        var input = JobInputBuilder.Cron("CronDetail", "CronGrp", cron: "0 0 8 * * ?");
        await _svc.AddScheduleJobAsync(input);

        var detail = await _svc.GetJobDetailAsync("CronGrp", "CronDetail");

        detail.Should().NotBeNull();
        detail!.TriggerType.Should().Be(1, "Cron 触发器类型为 1");
        detail.Cron.Should().Be("0 0 8 * * ?");
    }

    // ═══════════════════════════════════════════════════════════════
    // QueryJobAsync / QueryAllJobsAsync
    // ═══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "QueryJob - 应返回全部任务")]
    public async Task QueryJobAsync_ReturnsAllJobs()
    {
        for (var i = 1; i <= 5; i++)
            await _svc.AddScheduleJobAsync(JobInputBuilder.SimpleInterval($"PJob{i}", "PGroup"));

        var result = await _svc.QueryAllJobsAsync();

        result.Should().HaveCount(5, "共添加了 5 个任务，应全部返回");
    }

    [Fact(DisplayName = "QueryJob - 无筛选条件，返回全部任务")]
    public async Task QueryJobAsync_NoFilter_ReturnsAllItems()
    {
        for (var i = 1; i <= 5; i++)
            await _svc.AddScheduleJobAsync(JobInputBuilder.SimpleInterval($"PJob2_{i}", "PGroup2"));

        var result = await _svc.QueryAllJobsAsync();

        result.Should().HaveCount(5, "应一次返回全部 5 条");
    }

    [Fact(DisplayName = "QueryJob - 按任务名称模糊筛选，只返回匹配项")]
    public async Task QueryJobAsync_FilterByJobName_ReturnsMatchedOnly()
    {
        await _svc.AddScheduleJobAsync(JobInputBuilder.SimpleInterval("AlphaJob", "FGroup"));
        await _svc.AddScheduleJobAsync(JobInputBuilder.SimpleInterval("BetaJob", "FGroup"));
        await _svc.AddScheduleJobAsync(JobInputBuilder.SimpleInterval("AlphaJob2", "FGroup"));

        var result = await _svc.QueryAllJobsAsync(jobName: "Alpha");

        result.Should().OnlyContain(j => j.Name.Contains("Alpha"),
            "筛选 'Alpha' 应只返回包含该关键字的任务");
        result.Should().HaveCount(2);
    }

    [Fact(DisplayName = "QueryJob - 按分组筛选，只返回该分组的任务")]
    public async Task QueryJobAsync_FilterByJobGroup_ReturnsMatchedOnly()
    {
        await _svc.AddScheduleJobAsync(JobInputBuilder.SimpleInterval("GJob1", "GroupA"));
        await _svc.AddScheduleJobAsync(JobInputBuilder.SimpleInterval("GJob2", "GroupA"));
        await _svc.AddScheduleJobAsync(JobInputBuilder.SimpleInterval("GJob3", "GroupB"));

        var result = await _svc.QueryAllJobsAsync(jobGroup: "GroupA");

        result.Should().OnlyContain(j => j.GroupName == "GroupA");
        result.Should().HaveCount(2);
    }

    [Fact(DisplayName = "QueryJob - 调度器为空时，返回空列表")]
    public async Task QueryJobAsync_EmptyScheduler_ReturnsEmpty()
    {
        var result = await _svc.QueryAllJobsAsync();

        result.Should().BeEmpty();
    }
}

