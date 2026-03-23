using DotJob_Model;
using DotJob_Model.Entity;
using DotJob_Model.WebJobs;
using Host.Common;
using MySql.Data.MySqlClient;
using Quartz;

namespace Job_Scheduler.Application.Jobs;

/// <summary>
/// 调度中心服务，封装对 Quartz 调度器的常用操作：查询、添加、更新、删除、触发、日志查询等。
/// 该类作为应用服务被注入并由控制器调用。
/// </summary>
public class SchedulerCenterServices
{
    private readonly ISchedulerFactory _schedulerFactory;

    /// <summary>
    /// 构造函数，注入 Quartz 的 ISchedulerFactory 与 IServiceProvider 用于范围服务解析
    /// </summary>
    /// <param name="schedulerFactory">Quartz 调度器工厂</param>
    public SchedulerCenterServices(ISchedulerFactory schedulerFactory)
    {
        _schedulerFactory = schedulerFactory;
    }

    /// <summary>
    /// 获取 Quartz 调度器实例 受保护，允许子类覆盖
    /// </summary>
    /// <returns>IScheduler 异步实例</returns>
    protected virtual async Task<IScheduler> GetSchedulerAsync()
        => await _schedulerFactory.GetScheduler();

    /// <summary>
    /// 为内部使用获取 Scheduler 公开方法 返回当前调度器实例
    /// </summary>
    /// <returns>IScheduler 异步实例</returns>
    public async Task<IScheduler> GetSchedulerForInternalUseAsync()
        => await _schedulerFactory.GetScheduler();

    /// <summary>
    /// 关闭调度器
    /// </summary>
    /// <param name="waitForJobsToComplete">是否等待正在运行的任务完成后再停止</param>
    public async Task ShutdownAsync(bool waitForJobsToComplete = true)
    {
        var scheduler = await _schedulerFactory.GetScheduler();
        if (!scheduler.IsShutdown)
            await scheduler.Shutdown(waitForJobsToComplete);
    }
    
    /// <summary>
    /// 查询任务列表（服务端分页），直接读取 QRTZ_TRIGGERS 与 JOB_LOG 的汇总信息。
    /// 返回的每项包含任务名、分组、状态、上次/下次触发时间与运行次数统计。
    /// </summary>
    /// <param name="jobName">可选：按任务名模糊筛选</param>
    /// <param name="jobGroup">可选：按任务分组模糊筛选</param>
    /// <param name="pageNumber">页码（从1开始），默认 1</param>
    /// <param name="pageSize">每页数量，默认 20</param>
    /// <returns>分页的任务列表信息</returns>
    public virtual async Task<PageResponse<JobListInfo>> QueryJobAsync(
        string? jobName = null,
        string? jobGroup = null,
        int pageNumber = 1,
        int pageSize = 20)
    {
        await GetSchedulerAsync();

        var whereClauses = new List<string>
        {
            "t.SCHED_NAME = @schedName",
            "t.TRIGGER_NAME = t.JOB_NAME",
            "t.TRIGGER_GROUP = t.JOB_GROUP"
        };
        if (!string.IsNullOrWhiteSpace(jobName))  whereClauses.Add("t.JOB_NAME  COLLATE utf8mb4_unicode_ci LIKE @jobName");
        if (!string.IsNullOrWhiteSpace(jobGroup)) whereClauses.Add("t.JOB_GROUP COLLATE utf8mb4_unicode_ci LIKE @jobGroup");
        var where = string.Join(" AND ", whereClauses);

        await using var conn = new MySqlConnection(AppConfig.ConnectionString);
        await conn.OpenAsync();

        // 查询总数
        await using var countCmd = conn.CreateCommand();
        countCmd.CommandText = $"SELECT COUNT(*) FROM QRTZ_TRIGGERS t WHERE {where}";
        countCmd.Parameters.AddWithValue("@schedName", AppConfig.SchedulerName);
        if (!string.IsNullOrWhiteSpace(jobName))  countCmd.Parameters.AddWithValue("@jobName",  $"%{jobName}%");
        if (!string.IsNullOrWhiteSpace(jobGroup)) countCmd.Parameters.AddWithValue("@jobGroup", $"%{jobGroup}%");
        var total = Convert.ToInt32(await countCmd.ExecuteScalarAsync());

        // 查询分页数据，JOIN JOB_CONFIG 获取触发类型信息
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
SELECT t.JOB_NAME, t.JOB_GROUP, t.TRIGGER_STATE, t.NEXT_FIRE_TIME, t.PREV_FIRE_TIME,
       c.TRIGGER_TYPE, c.CRON, c.INTERVAL_SECOND
FROM QRTZ_TRIGGERS t
LEFT JOIN JOB_CONFIG c
       ON c.JOB_NAME  COLLATE utf8mb4_unicode_ci = t.JOB_NAME  COLLATE utf8mb4_unicode_ci
      AND c.JOB_GROUP COLLATE utf8mb4_unicode_ci = t.JOB_GROUP COLLATE utf8mb4_unicode_ci
WHERE {where}
ORDER BY t.JOB_GROUP, t.JOB_NAME
LIMIT @offset, @size";
        cmd.Parameters.AddWithValue("@schedName", AppConfig.SchedulerName);
        if (!string.IsNullOrWhiteSpace(jobName))  cmd.Parameters.AddWithValue("@jobName",  $"%{jobName}%");
        if (!string.IsNullOrWhiteSpace(jobGroup)) cmd.Parameters.AddWithValue("@jobGroup", $"%{jobGroup}%");
        cmd.Parameters.AddWithValue("@offset", (pageNumber - 1) * pageSize);
        cmd.Parameters.AddWithValue("@size",   pageSize);

        static DateTime? TicksToLocal(long? ticks)
        {
            if (ticks == null || ticks <= 0) return null;
            try { return new DateTime(ticks.Value, DateTimeKind.Utc).ToLocalTime(); }
            catch { return null; }
        }

        var items = new List<JobListInfo>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var stateStr = reader.IsDBNull(2) ? "NONE" : reader.GetString(2);
            var nextMs   = reader.IsDBNull(3) ? (long?)null : reader.GetInt64(3);
            var prevMs   = reader.IsDBNull(4) ? (long?)null : reader.GetInt64(4);

            items.Add(new JobListInfo
            {
                Name         = reader.GetString(0),
                GroupName    = reader.GetString(1),
                TriggerState = stateStr.ToUpperInvariant() switch
                {
                    "WAITING" or "ACQUIRED" or "EXECUTING" => 1,
                    "PAUSED"   => 2,
                    "BLOCKED"  => 5,
                    "ERROR"    => 4,
                    "COMPLETE" => 3,
                    _          => 0
                },
                NextFireTime     = TicksToLocal(nextMs),
                PreviousFireTime = TicksToLocal(prevMs),
                TriggerType      = reader.IsDBNull(5) ? 0 : reader.GetInt32(5),
                Cron             = reader.IsDBNull(6) ? null : reader.GetString(6),
                IntervalSecond   = reader.IsDBNull(7) ? null : reader.GetInt32(7),
            });
        }

        return new PageResponse<JobListInfo>
        {
            Data     = items,
            PageInfo = new PageInfo { Total = total, PageSize = pageSize, PageNumber = pageNumber }
        };
    }

    /// <summary>
    /// 查询所有任务（不分页），用于统计汇总或批量删除等操作
    /// </summary>
    /// <param name="jobName">可选：按任务名模糊筛选</param>
    /// <param name="jobGroup">可选：按任务分组模糊筛选</param>
    /// <returns>任务列表信息集合</returns>
    public virtual async Task<List<JobListInfo>> QueryAllJobsAsync(string? jobName = null, string? jobGroup = null)
    {
        var response = await QueryJobAsync(jobName, jobGroup, 1, int.MaxValue);
        return response.Data;
    }
    
    /// <summary>
    /// 暂停指定的 Job 立即将该 Job 标记为 Paused
    /// </summary>
    /// <param name="jobGroup">任务分组</param>
    /// <param name="jobName">任务名称</param>
    public async Task PauseJonAsync(string jobGroup, string jobName)
    {
        var scheduler = await GetSchedulerAsync();
        await scheduler.PauseJob(new JobKey(jobName, jobGroup));
    }

    /// <summary>
    /// 立即触发指定 Job 不会影响原有调度计划
    /// 通过 JobDataMap 传入 manual_trigger=true 标记
    /// JobBase 检测到该标记后将跳过结束时间与执行次数上限检查 确保强制执行一次
    /// </summary>
    /// <param name="jobGroup">任务分组</param>
    /// <param name="jobName">任务名称</param>
    /// <exception cref="Exception">当任务不存在时抛出异常</exception>
    public async Task TriggerJobNowAsync(string jobGroup, string jobName)
    {
        var scheduler = await GetSchedulerAsync();
        var jobKey = new JobKey(jobName, jobGroup);
        if (!await scheduler.CheckExists(jobKey))
            throw new Exception($"任务不存在: {jobGroup}.{jobName}");

        // 传入手动触发标记，JobBase 会据此跳过结束时间与次数限制检查
        var data = new JobDataMap { { "manual_trigger", "true" } };
        await scheduler.TriggerJob(jobKey, data);
    }

    /// <summary>
    /// 恢复指定任务 从 Paused 状态恢复为可调度状态 会检查任务结束时间 若结束时间已过将抛出异常
    /// </summary>
    /// <param name="jobGroup">任务分组</param>
    /// <param name="jobName">任务名称</param>
    /// <exception cref="Exception">当任务结束时间已过时抛出异常</exception>
    public async Task ResumeJobAsync(string jobGroup, string jobName)
    {
        var scheduler = await GetSchedulerAsync();
        var jobKey = new JobKey(jobName, jobGroup);
        if (!await scheduler.CheckExists(jobKey))
            return;

        // 检查结束时间
        var endTime = await GetJobEndTimeAsync(jobName, jobGroup);
        if (endTime != null && endTime.Value <= DateTime.Now)
            throw new Exception("Cannot resume job because its end time has passed.");

        await scheduler.ResumeJob(jobKey);
    }

    /// <summary>
    /// 删除任务 同时删除 JOB_LOG 与 JOB_CONFIG 中的相关记录
    /// </summary>
    /// <param name="jobGroup">任务分组</param>
    /// <param name="jobName">任务名称</param>
    public async Task DelJobAsync(string jobGroup, string jobName)
    {
        var scheduler = await GetSchedulerAsync();
        await scheduler.PauseJob(new JobKey(jobName, jobGroup));
        await scheduler.DeleteJob(new JobKey(jobName, jobGroup));
        await DeleteJobDataAsync(jobGroup, jobName);
    }

    /// <summary>
    /// 删除数据库中任务相关记录（JOB_LOG、JOB_CONFIG），protected virtual 供测试覆盖
    /// </summary>
    /// <param name="jobGroup">任务分组</param>
    /// <param name="jobName">任务名称</param>
    protected virtual async Task DeleteJobDataAsync(string jobGroup, string jobName)
    {
        var fullJobName = $"{jobGroup}.{jobName}";
        await using var conn = new MySqlConnection(AppConfig.ConnectionString);
        await conn.OpenAsync();

        await using var delLog = conn.CreateCommand();
        delLog.CommandText = "DELETE FROM JOB_LOG WHERE JOB_NAME = @jobName";
        delLog.Parameters.AddWithValue("@jobName", fullJobName);
        await delLog.ExecuteNonQueryAsync();

        await using var delCfg = conn.CreateCommand();
        delCfg.CommandText = "DELETE FROM JOB_CONFIG WHERE JOB_NAME = @jobName AND JOB_GROUP = @jobGroup";
        delCfg.Parameters.AddWithValue("@jobName", jobName);
        delCfg.Parameters.AddWithValue("@jobGroup", jobGroup);
        await delCfg.ExecuteNonQueryAsync();
    }


    /// <summary>
    /// 添加任务到调度器：
    /// 1. 写入或更新 JOB_CONFIG 表；
    /// 2. 在 Quartz 中构建 Job 与 Trigger 并调度。
    /// </summary>
    /// <param name="input">任务配置信息</param>
    /// <exception cref="Exception">当任务已存在时抛出</exception>
    public async Task AddScheduleJobAsync(AddWebJobs input)
    {
        var scheduler = await GetSchedulerAsync();
        var jobKey = new JobKey(input.JobName, input.JobGroup);
        if (await scheduler.CheckExists(jobKey))
            throw new Exception("Job already exists");

        await UpsertJobConfigAsync(input);

        var job = JobBuilder.Create<HttpJob>().WithIdentity(input.JobName, input.JobGroup).Build();
        var trigger = input.TriggerType == TriggerTypeEnum.Cron
            ? CreateCronTrigger(input)
            : CreateSimpleTrigger(input);
        await scheduler.ScheduleJob(job, trigger);
    }

    /// <summary>
    /// 更新任务：替换 Quartz 中的 Job 与 Trigger，并保留原来是否处于暂停状态
    /// </summary>
    /// <param name="input">任务配置信息（任务名与分组不可修改）</param>
    public async Task UpdateScheduleJobAsync(AddWebJobs input)
    {
        var scheduler = await GetSchedulerAsync();
        var jobKey = new JobKey(input.JobName, input.JobGroup);
        if (!await scheduler.CheckExists(jobKey))
            throw new Exception("Job does not exist");

        // 记录修改前是否暂停
        var wasPaused = false;
        var existingTriggers = await scheduler.GetTriggersOfJob(jobKey);
        var existingTrigger = existingTriggers.FirstOrDefault();
        if (existingTrigger != null)
        {
            var state = await scheduler.GetTriggerState(existingTrigger.Key);
            wasPaused = state == TriggerState.Paused;
        }

        await UpsertJobConfigAsync(input);

        await scheduler.PauseJob(jobKey);
        await scheduler.DeleteJob(jobKey);

        var job = JobBuilder.Create<HttpJob>().WithIdentity(input.JobName, input.JobGroup).Build();
        ITrigger trigger = input.TriggerType == TriggerTypeEnum.Cron
            ? CreateCronTrigger(input)
            : CreateSimpleTrigger(input);
        await scheduler.ScheduleJob(job, trigger);

        if (wasPaused)
            await scheduler.PauseJob(jobKey);
    }
    
    /// <summary>
    /// 查询指定任务的日志（分页）
    /// </summary>
    /// <param name="jobName">任务名称</param>
    /// <param name="jobGroup">任务分组</param>
    /// <param name="pageNumber">页码（从1开始）</param>
    /// <param name="pageSize">每页数目</param>
    /// <returns>分页的日志集合与分页信息</returns>
    public async Task<PageResponse<LogEntity>> QueryJobLogsAsync(string jobName, string jobGroup, int pageNumber = 1, int pageSize = 20)
    {
        var fullJobName = $"{jobGroup}.{jobName}";
        await using var conn = new MySqlConnection(AppConfig.ConnectionString);
        await conn.OpenAsync();

        await using var countCmd = conn.CreateCommand();
        countCmd.CommandText = "SELECT COUNT(*) FROM JOB_LOG WHERE JOB_NAME = @jobName";
        countCmd.Parameters.AddWithValue("@jobName", fullJobName);
        var total = Convert.ToInt32(await countCmd.ExecuteScalarAsync());

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT ID, BEGIN_TIME, END_TIME, JOB_NAME, JOB_GROUP, ERROR_MSG,
                                   EXECUTE_TIME, EXECUTION_STATUS, URL, REQUEST_TYPE, PARAMETERS, RESULT, STATUS_CODE
                            FROM JOB_LOG WHERE JOB_NAME = @jobName
                            ORDER BY BEGIN_TIME DESC LIMIT @offset, @size";
        cmd.Parameters.AddWithValue("@jobName", fullJobName);
        cmd.Parameters.AddWithValue("@offset", (pageNumber - 1) * pageSize);
        cmd.Parameters.AddWithValue("@size", pageSize);

        await using var reader = await cmd.ExecuteReaderAsync();
        var logs = new List<LogEntity>();
        while (await reader.ReadAsync())
        {
            logs.Add(new LogEntity
            {
                Id = reader.GetInt64(0),
                BeginTime = reader.GetDateTime(1),
                EndTime = reader.GetDateTime(2),
                JobName = reader.IsDBNull(3) ? null : reader.GetString(3),
                JobGroup = reader.IsDBNull(4) ? null : reader.GetString(4),
                ErrorMsg = reader.IsDBNull(5) ? null : reader.GetString(5),
                ExecuteTime = reader.GetDouble(6),
                ExecutionStatus = reader.GetInt32(7),
                Url = reader.IsDBNull(8) ? null : reader.GetString(8),
                RequestType = reader.IsDBNull(9) ? null : reader.GetString(9),
                Parameters = reader.IsDBNull(10) ? null : reader.GetString(10),
                Result = reader.IsDBNull(11) ? null : reader.GetString(11),
                StatusCode = reader.IsDBNull(12) ? null : reader.GetInt32(12)
            });
        }

        return new PageResponse<LogEntity>
        {
            Data = logs,
            PageInfo = new PageInfo { Total = total, PageSize = pageSize, PageNumber = pageNumber }
        };
    }
    
    /// <summary>
    /// 获取任务详情（组合 JOB_CONFIG 与 Quartz Trigger 信息）
    /// </summary>
    /// <param name="jobGroup">任务分组</param>
    /// <param name="jobName">任务名称</param>
    /// <returns>任务详情信息或 null（任务不存在）</returns>
    public async Task<JobDetailInfo?> GetJobDetailAsync(string jobGroup, string jobName)
    {
        var scheduler = await GetSchedulerAsync();
        var jobKey = new JobKey(jobName, jobGroup);
        if (!await scheduler.CheckExists(jobKey)) return null;

        var triggers = await scheduler.GetTriggersOfJob(jobKey);
        var trigger = triggers.FirstOrDefault();
        var triggerState = trigger != null ? await scheduler.GetTriggerState(trigger.Key) : TriggerState.None;

        var triggerType = 1;
        var cron = "";
        var intervalSecond = 0;
        if (trigger is ICronTrigger ct)
        {
            triggerType = 1;
            cron = ct.CronExpressionString ?? "";
        }
        else if (trigger is ISimpleTrigger st)
        {
            triggerType = 2;
            intervalSecond = (int)st.RepeatInterval.TotalSeconds;
        }

        // 从 JOB_CONFIG 读配置，从 JOB_LOG 统计运行次数
        var config = await LoadJobConfigAsync(jobGroup, jobName);
        var runNumber = await CountLogsAsync($"{jobGroup}.{jobName}");

        var triggerStateInt = triggerState switch
        {
            TriggerState.Normal => 1,
            TriggerState.Paused => 2,
            TriggerState.Complete => 3,
            TriggerState.Error => 4,
            TriggerState.Blocked => 5,
            _ => 0
        };

        return new JobDetailInfo
        {
            JobName = jobName,
            JobGroup = jobGroup,
            Description = config?.Description ?? "",
            TriggerType = triggerType,
            Cron = cron,
            IntervalSecond = intervalSecond,
            TriggerState = triggerStateInt,
            RequestUrl = config?.RequestUrl ?? "",
            RequestType = config?.RequestType ?? 0,
            Headers = config?.Headers ?? "",
            RequestParameters = config?.RequestParameters ?? "",
            RunNumber = runNumber,
            BeginTime = config?.BeginTime,
            EndTime = config?.EndTime,
            RunTotal = config?.RunTotal > 0 ? config.RunTotal : null,
            NextFireTime = trigger?.GetNextFireTimeUtc()?.LocalDateTime,
            PreviousFireTime = trigger?.GetPreviousFireTimeUtc()?.LocalDateTime,
            MailMessage = config?.MailMessage ?? 0,
            Dingtalk = config?.Dingtalk ?? 0
        };
    }

    /// <summary>
    /// 添加或更新 JOB_CONFIG 表中的配置（Upsert）
    /// </summary>
    /// <param name="input">AddWebJobs 输入 DTO</param>
    protected virtual async Task UpsertJobConfigAsync(AddWebJobs input)
    {
        await using var conn = new MySqlConnection(AppConfig.ConnectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"INSERT INTO JOB_CONFIG
            (JOB_NAME, JOB_GROUP, DESCRIPTION, REQUEST_URL, REQUEST_TYPE, HEADERS, REQUEST_PARAMETERS,
             TRIGGER_TYPE, CRON, INTERVAL_SECOND, BEGIN_TIME, END_TIME, RUN_TOTAL, DINGTALK, MAIL_MESSAGE, CREATED_AT, UPDATED_AT)
            VALUES
            (@jobName, @jobGroup, @desc, @url, @reqType, @headers, @params,
             @trigType, @cron, @interval, @beginTime, @endTime, @runTotal, @dingtalk, @mail, @now, @now)
            ON DUPLICATE KEY UPDATE
            DESCRIPTION=@desc, REQUEST_URL=@url, REQUEST_TYPE=@reqType, HEADERS=@headers, REQUEST_PARAMETERS=@params,
            TRIGGER_TYPE=@trigType, CRON=@cron, INTERVAL_SECOND=@interval,
            BEGIN_TIME=@beginTime, END_TIME=@endTime, RUN_TOTAL=@runTotal,
            DINGTALK=@dingtalk, MAIL_MESSAGE=@mail, UPDATED_AT=@now";

        var now = DateTime.Now;
        cmd.Parameters.AddWithValue("@jobName", input.JobName);
        cmd.Parameters.AddWithValue("@jobGroup", input.JobGroup);
        cmd.Parameters.AddWithValue("@desc", (object?)input.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@url", input.RequestUrl ?? "");
        cmd.Parameters.AddWithValue("@reqType", (int)input.RequestType);
        cmd.Parameters.AddWithValue("@headers", (object?)input.Headers ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@params", (object?)input.RequestParameters ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@trigType", (int)input.TriggerType);
        cmd.Parameters.AddWithValue("@cron", (object?)input.Cron ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@interval", (object?)input.IntervalSecond ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@beginTime", (object?)input.BeginTime.LocalDateTime ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@endTime", (object?)input.EndTime?.LocalDateTime ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@runTotal", (object?)input.RunTotal ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@dingtalk", input.Dingtalk);
        cmd.Parameters.AddWithValue("@mail", input.MailMessage);
        cmd.Parameters.AddWithValue("@now", now);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// 从 JOB_CONFIG 加载配置（与 JobBase 共用同一 SQL 逻辑）
    /// </summary>
    /// <param name="jobGroup">任务分组</param>
    /// <param name="jobName">任务名</param>
    /// <returns>找到则返回 <see cref="JobConfig"/>，否则返回 null</returns>
    protected virtual async Task<JobConfig?> LoadJobConfigAsync(string jobGroup, string jobName)
    {
        try
        {
            await using var conn = new MySqlConnection(AppConfig.ConnectionString);
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT * FROM JOB_CONFIG WHERE JOB_NAME = @jobName AND JOB_GROUP = @jobGroup LIMIT 1";
            cmd.Parameters.AddWithValue("@jobName", jobName);
            cmd.Parameters.AddWithValue("@jobGroup", jobGroup);
            await using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) return null;

            int Ord(string col) => reader.GetOrdinal(col);

            string? NullStr(string col)
            {
                var i = Ord(col);
                return reader.IsDBNull(i) ? null : reader.GetString(i);
            }

            int? NullInt(string col)
            {
                var i = Ord(col);
                return reader.IsDBNull(i) ? null : reader.GetInt32(i);
            }

            DateTime? NullDt(string col)
            {
                var i = Ord(col);
                return reader.IsDBNull(i) ? null : reader.GetDateTime(i);
            }

            return new JobConfig
            {
                JobName = reader.GetString(Ord("JOB_NAME")),
                JobGroup = reader.GetString(Ord("JOB_GROUP")),
                Description = NullStr("DESCRIPTION"),
                RequestUrl = NullStr("REQUEST_URL") ?? "",
                RequestType = reader.GetInt32(Ord("REQUEST_TYPE")),
                Headers = NullStr("HEADERS"),
                RequestParameters = NullStr("REQUEST_PARAMETERS"),
                TriggerType = reader.GetInt32(Ord("TRIGGER_TYPE")),
                Cron = NullStr("CRON"),
                IntervalSecond = NullInt("INTERVAL_SECOND"),
                BeginTime = NullDt("BEGIN_TIME"),
                EndTime = NullDt("END_TIME"),
                RunTotal = NullInt("RUN_TOTAL"),
                Dingtalk = reader.GetInt32(Ord("DINGTALK")),
                MailMessage = reader.GetInt32(Ord("MAIL_MESSAGE")),
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LoadJobConfig] 失败: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 统计任务执行次数（从 JOB_LOG 中按 jobName 聚合）
    /// </summary>
    /// <param name="fullJobName">形如 "Group.Name" 的完整任务名</param>
    /// <returns>执行次数（int）</returns>
    protected virtual async Task<int> CountLogsAsync(string fullJobName)
    {
        await using var conn = new MySqlConnection(AppConfig.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM JOB_LOG WHERE JOB_NAME = @jobName";
        cmd.Parameters.AddWithValue("@jobName", fullJobName);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    /// <summary>
    /// 获取指定任务在 JOB_CONFIG 中的结束时间（如果存在）
    /// </summary>
    /// <param name="jobName">任务名称</param>
    /// <param name="jobGroup">任务分组</param>
    /// <returns>若配置了结束时间返回 DateTime，否则返回 null</returns>
    protected virtual async Task<DateTime?> GetJobEndTimeAsync(string jobName, string jobGroup)
    {
        await using var conn = new MySqlConnection(AppConfig.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT END_TIME FROM JOB_CONFIG WHERE JOB_NAME = @jobName AND JOB_GROUP = @jobGroup LIMIT 1";
        cmd.Parameters.AddWithValue("@jobName", jobName);
        cmd.Parameters.AddWithValue("@jobGroup", jobGroup);
        var val = await cmd.ExecuteScalarAsync();
        return val == DBNull.Value || val == null ? null : (DateTime?)Convert.ToDateTime(val);
    }
    
    /// <summary>
    /// 创建简单（固定间隔）类型的 Trigger
    /// </summary>
    /// <param name="input">AddWebJobs 输入 DTO，包含间隔、开始/结束时间等</param>
    /// <returns>构建好的 ITrigger 实例</returns>
    private ITrigger CreateSimpleTrigger(AddWebJobs input)
    {
        var startAt = input.BeginTime > DateTimeOffset.UtcNow ? input.BeginTime : DateTimeOffset.UtcNow;
        DateTimeOffset? endAt = input.EndTime.HasValue && input.EndTime.Value > DateTimeOffset.UtcNow
            ? input.EndTime
            : null;

        return TriggerBuilder.Create()
            .WithIdentity(input.JobName, input.JobGroup)
            .StartAt(startAt)
            .WithSimpleSchedule(x => x
                .WithIntervalInSeconds(input.IntervalSecond!.Value)
                .RepeatForever()
                .WithMisfireHandlingInstructionFireNow())
            .ForJob(input.JobName, input.JobGroup)
            .Build();
    }

    /// <summary>
    /// 创建 Cron 类型的 Trigger
    /// </summary>
    /// <param name="entity">AddWebJobs 输入 DTO，包含 Cron 表达式及开始/结束时间</param>
    /// <returns>构建好的 ITrigger 实例</returns>
    private ITrigger CreateCronTrigger(AddWebJobs entity)
    {
        var startAt = entity.BeginTime > DateTimeOffset.UtcNow ? entity.BeginTime : DateTimeOffset.UtcNow;
        DateTimeOffset? endAt = entity.EndTime.HasValue && entity.EndTime.Value > DateTimeOffset.UtcNow
            ? entity.EndTime
            : null;

        return TriggerBuilder.Create()
            .WithIdentity(entity.JobName, entity.JobGroup)
            .StartAt(startAt)
            .WithCronSchedule(entity.Cron,
                x => x.WithMisfireHandlingInstructionFireAndProceed())
            .ForJob(entity.JobName, entity.JobGroup)
            .Build();
    }
}
