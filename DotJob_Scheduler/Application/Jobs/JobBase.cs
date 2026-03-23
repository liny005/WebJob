using System.Diagnostics;
using DotJob_Model.Entity;
using DotJob_Model.Enums;
using Host;
using Job_Scheduler.Application.Notify;
using Microsoft.Extensions.DependencyInjection;
using MySql.Data.MySqlClient;
using Quartz;

namespace Job_Scheduler.Application.Jobs;

public abstract class JobBase : IDisposable
{
    protected NoticeEnum MailLevel = NoticeEnum.None;

    protected NoticeEnum Dingtalk = NoticeEnum.None;

    protected LogEntity LogInfo { get; private set; }

    /// <summary>从 JOB_CONFIG 加载的任务配置，子类在 NextExecute 中使用</summary>
    protected JobConfig? JobConfig { get; private set; }

    private readonly Stopwatch _stopwatch = new();
    private readonly IServiceProvider _serviceProvider;

    public JobBase(LogEntity logInfo, IServiceProvider serviceProvider)
    {
        LogInfo = logInfo;
        _serviceProvider = serviceProvider;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var jobName  = context.JobDetail.Key.Name;
        var jobGroup = context.JobDetail.Key.Group;
        var fullJobName = $"{jobGroup}.{jobName}";

        var entryTime = DateTime.Now;

        // 诊断：排队延迟
        var scheduledTime   = context.ScheduledFireTimeUtc?.LocalDateTime;
        var threadEntryLag  = scheduledTime.HasValue ? (entryTime - scheduledTime.Value).TotalSeconds : -1;
        Console.WriteLine($"[SLOW] {fullJobName} | 计划={scheduledTime:HH:mm:ss} 实际进入={entryTime:HH:mm:ss} | 排队延迟={threadEntryLag:F1}s | 线程={Environment.CurrentManagedThreadId}");

        // 1. 从 JOB_CONFIG 加载任务配置
        JobConfig = await LoadJobConfigAsync(jobGroup, jobName);

        // 判断是否为手动触发，手动触发时跳过结束时间与次数限制
        var isManualTrigger = context.MergedJobDataMap.TryGetValue("manual_trigger", out var manualFlag)
                              && manualFlag?.ToString() == "true";

        // 2. 结束时间检查（手动触发时跳过）
        if (!isManualTrigger && JobConfig?.EndTime != null && JobConfig.EndTime.Value <= DateTime.Now)
        {
            await context.Scheduler.PauseJob(new JobKey(jobName, jobGroup));
            return;
        }

        // 3. 执行次数上限检查（手动触发时跳过）
        if (!isManualTrigger && JobConfig?.RunTotal is > 0)
        {
            var executedCount = await CountLogsAsync(fullJobName);
            if (executedCount >= JobConfig.RunTotal.Value)
            {
                await context.Scheduler.PauseJob(new JobKey(jobName, jobGroup));
                return;
            }
        }

        // 4. 通知级别
        MailLevel = (NoticeEnum)(JobConfig?.MailMessage ?? 0);
        Dingtalk  = (NoticeEnum)(JobConfig?.Dingtalk    ?? 0);

        // 5. 填充日志基本信息
        LogInfo.BeginTime       = entryTime;
        LogInfo.JobName         = fullJobName;
        LogInfo.JobGroup        = jobGroup;
        LogInfo.ExecutionStatus = 1;
        LogInfo.Url             = JobConfig?.RequestUrl;
        LogInfo.RequestType     = JobConfig != null ? ((RequestTypeEnum)JobConfig.RequestType).ToString() : null;
        LogInfo.Parameters      = JobConfig?.RequestParameters;

        _stopwatch.Restart();

        try
        {
            await NextExecute(context);
        }
        catch (Exception e)
        {
            LogInfo.ErrorMsg        = e.Message;
            LogInfo.ExecutionStatus = 2;
        }
        finally
        {
            _stopwatch.Stop();
            LogInfo.EndTime     = DateTime.Now;
            LogInfo.ExecuteTime = _stopwatch.Elapsed.TotalSeconds;
            await SaveLogAsync();

            // 6. 统一发送通知（钉钉 + 邮件）
            await SendNotificationsAsync(fullJobName);
        }
    }

    public abstract Task NextExecute(IJobExecutionContext context);

    /// <summary>
    /// 执行完毕后根据各渠道的通知级别决定是否推送。
    /// 钉钉由 Dingtalk 字段控制，邮件由 MailLevel 字段控制，互相独立。
    /// </summary>
    private async Task SendNotificationsAsync(string fullJobName)
    {
        var isSuccess    = LogInfo.ExecutionStatus == 1;
        var requestUrl   = LogInfo.Url   ?? "";
        var result       = LogInfo.Result ?? "";

        var shouldDingTalk = Dingtalk  == NoticeEnum.All || (Dingtalk  == NoticeEnum.Err && !isSuccess);
        var shouldEmail    = MailLevel == NoticeEnum.All || (MailLevel == NoticeEnum.Err && !isSuccess);

        if (!shouldDingTalk && !shouldEmail) return;

        try
        {
            using var scope       = _serviceProvider.CreateScope();
            var notifyService     = scope.ServiceProvider.GetRequiredService<NotifyService>();
            var dingTalkService   = scope.ServiceProvider.GetRequiredService<DingTalkService>();
            var emailService      = scope.ServiceProvider.GetRequiredService<EmailService>();

            // 拉取所有已启用的推送配置
            var configs = await notifyService.GetAllAsync();
            var enabled = configs.Where(c => c.IsEnabled).ToList();

            foreach (var config in enabled)
            {
                try
                {
                    if (config.Channel == "DingTalk" && shouldDingTalk)
                        await dingTalkService.SendAsync(config, fullJobName, requestUrl, result, isSuccess);
                    else if (config.Channel == "Email" && shouldEmail)
                        await emailService.SendAsync(config, fullJobName, requestUrl, result, isSuccess);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Notify] 推送失败 [{config.Channel}][{config.Name}]: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Notify] 获取推送配置失败: {ex.Message}");
        }
    }



    /// <summary>
    /// 从 JOB_CONFIG 表加载任务配置（短连接，用完立即释放回连接池）
    /// </summary>
    private static async Task<JobConfig?> LoadJobConfigAsync(string jobGroup, string jobName)
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
            Console.WriteLine($"[LoadJobConfigAsync] 加载配置失败: {ex.Message}");
            return null;
        }
    }

    /// <summary>统计已执行次数（短连接）</summary>
    private static async Task<long> CountLogsAsync(string jobName)
    {
        try
        {
            await using var conn = new MySqlConnection(AppConfig.ConnectionString);
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM JOB_LOG WHERE JOB_NAME = @jobName";
            cmd.Parameters.AddWithValue("@jobName", jobName);
            var result = await cmd.ExecuteScalarAsync();
            return Convert.ToInt64(result);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CountLogsAsync] 查询失败: {ex.Message}");
            return 0;
        }
    }

    /// <summary>执行完毕后一次性写入完整日志（短连接）</summary>
    private async Task SaveLogAsync()
    {
        try
        {
            await using var conn = new MySqlConnection(AppConfig.ConnectionString);
            await conn.OpenAsync();

            const string sql = @"INSERT INTO JOB_LOG
                (BEGIN_TIME, END_TIME, JOB_NAME, JOB_GROUP, ERROR_MSG, EXECUTE_TIME, EXECUTION_STATUS, URL, REQUEST_TYPE, PARAMETERS, RESULT, STATUS_CODE)
                VALUES
                (@beginTime, @endTime, @jobName, @jobGroup, @errorMsg, @executeTime, @executionStatus, @url, @requestType, @parameters, @result, @statusCode)";

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("@beginTime", LogInfo.BeginTime);
            cmd.Parameters.AddWithValue("@endTime", LogInfo.EndTime);
            cmd.Parameters.AddWithValue("@jobName", (object?)LogInfo.JobName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@jobGroup", (object?)LogInfo.JobGroup ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@errorMsg", (object?)LogInfo.ErrorMsg ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@executeTime", LogInfo.ExecuteTime);
            cmd.Parameters.AddWithValue("@executionStatus", LogInfo.ExecutionStatus);
            cmd.Parameters.AddWithValue("@url", (object?)LogInfo.Url ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@requestType", (object?)LogInfo.RequestType ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@parameters", (object?)LogInfo.Parameters ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@result", (object?)LogInfo.Result ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@statusCode", (object?)LogInfo.StatusCode ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SaveLogAsync] 写日志失败: {ex.Message}");
        }
    }

    public void Dispose()
    {
    }
}