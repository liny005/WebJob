using System.Text.Json;
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

    #region Echo 测试接口（验证 Headers / 请求参数能否正确接收）

    /// <summary>
    /// 回显收到的 HTTP Headers（JSON 格式）
    /// </summary>
    [HttpGet("echo/headers")]
    public IActionResult EchoHeaders()
    {
        var headers = Request.Headers.ToDictionary(
            h => h.Key,
            h => h.Value.ToString());

        return Ok(new
        {
            method = Request.Method,
            headers
        });
    }

    /// <summary>
    /// 回显收到的 URL 查询参数（GET 请求参数测试）
    /// </summary>
    [HttpGet("echo/query")]
    public IActionResult EchoQuery([FromQuery] string? payload = null)
    {
        var result = new Dictionary<string, object?>
        {
            ["method"] = Request.Method,
            ["query"] = Request.Query.ToDictionary(q => q.Key, q => q.Value.ToString())
        };

        if (!string.IsNullOrWhiteSpace(payload))
        {
            try { result["parsedPayload"] = JsonSerializer.Deserialize<object>(payload); }
            catch { result["parsedPayload"] = payload; }
        }

        return Ok(result);
    }

    /// <summary>
    /// 回显收到的请求体（POST / PUT 请求参数 JSON 格式测试）
    /// </summary>
    [HttpPost("echo/body")]
    [HttpPut("echo/body")]
    public async Task<IActionResult> EchoBody()
    {
        using var reader = new StreamReader(Request.Body);
        var body = await reader.ReadToEndAsync();

        object? parsed = null;
        if (!string.IsNullOrWhiteSpace(body))
        {
            try { parsed = JsonSerializer.Deserialize<object>(body); }
            catch { parsed = body; }
        }

        return Ok(new
        {
            method = Request.Method,
            body = parsed
        });
    }

    /// <summary>
    /// 通用回显：返回 Method + Headers + Query/Body
    /// </summary>
    [HttpGet("echo")]
    [HttpPost("echo")]
    [HttpPut("echo")]
    [HttpDelete("echo")]
    public async Task<IActionResult> Echo()
    {
        var headers = Request.Headers.ToDictionary(
            h => h.Key,
            h => h.Value.ToString());

        var query = Request.Query.ToDictionary(q => q.Key, q => q.Value.ToString());

        object? body = null;
        if (Request.Method is "POST" or "PUT" && Request.ContentLength > 0)
        {
            using var reader = new StreamReader(Request.Body);
            var raw = await reader.ReadToEndAsync();
            if (!string.IsNullOrWhiteSpace(raw))
            {
                try { body = JsonSerializer.Deserialize<object>(raw); }
                catch { body = raw; }
            }
        }

        return Ok(new
        {
            method = Request.Method,
            headers,
            query,
            body
        });
    }

    #endregion

    /// <summary>
    /// 批量生成定时任务，用于批量添加接口测试
    /// 触发类型随机为 Cron 或 Simple，间隔时间范围 10-60 秒，部分任务随机带执行次数限制
    /// 任务目标指向本控制器提供的 Echo 测试接口，可验证 Headers 与 JSON 请求参数是否正确传递
    /// </summary>
    /// <param name="count">生成任务数量，默认随机 100-200 个（范围建议 100-200）</param>
    /// <param name="baseUrl">测试接口基地址，默认使用当前请求的 Scheme://Host</param>
    [HttpGet("batch-create-jobs")]
    public async Task<object> BatchCreateJobsAsync([FromQuery] int? count = null, [FromQuery] string? baseUrl = null)
    {
        var random = new Random();
        var total = count ?? random.Next(100, 201); // 默认 100-200 个
        total = Math.Clamp(total, 1, 1000);

        // 未指定基地址时，使用当前请求的地址作为回显目标
        baseUrl ??= $"{Request.Scheme}://{Request.Host}";
        baseUrl = baseUrl.TrimEnd('/');

        var groups = new[] { "测试分组A", "测试分组B", "测试分组C", "测试分组D" };
        var requestTypes = new[] { RequestTypeEnum.Get, RequestTypeEnum.Post, RequestTypeEnum.Put, RequestTypeEnum.Delete };

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

            // 随机请求方法、Headers 与请求参数
            var requestType = requestTypes[random.Next(requestTypes.Length)];
            var headersJson = GenerateRandomHeaders(random);
            var parametersJson = GenerateRandomParameters(random);
            var requestUrl = BuildEchoUrl(baseUrl, requestType, parametersJson);

            var input = new AddWebJobs
            {
                JobName           = jobName,
                JobGroup          = jobGroup,
                JobType           = JobTypeEnum.Url,
                TriggerType       = triggerType,
                IntervalSecond    = intervalSec,
                Cron              = cronExpr,
                RequestType       = requestType,
                RequestUrl        = requestUrl,
                Headers           = headersJson,
                RequestParameters = requestType is RequestTypeEnum.Post or RequestTypeEnum.Put ? parametersJson : string.Empty,
                Description       = $"批量测试任务 #{i}，方法 {requestType}，{(isCron ? "cron:" + cronExpr : "间隔 " + intervalSec + "s")}" +
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

    /// <summary>
    /// 清空所有任务及任务日志（测试环境清理）
    /// </summary>
    [HttpGet("clear-all-jobs")]
    public async Task<object> ClearAllJobsAsync()
    {
        var (deletedJobs, deletedLogs) = await _schedulerCenterServices.DeleteAllJobsAsync();
        return new
        {
            Message = "已清空所有任务及日志",
            DeletedJobs = deletedJobs,
            DeletedLogs = deletedLogs
        };
    }

    #region 私有辅助方法

    /// <summary>
    /// 生成随机 Headers（JSON 字符串），包含测试标识便于追踪
    /// </summary>
    private static string GenerateRandomHeaders(Random random)
    {
        var headers = new Dictionary<string, string>
        {
            ["X-Test-Id"]   = Guid.NewGuid().ToString("N"),
            ["X-Job-Index"] = random.Next(1, 1_000_000).ToString(),
            ["X-Source"]    = "DotJob-Scheduler-Batch"
        };
        return JsonSerializer.Serialize(headers);
    }

    /// <summary>
    /// 生成随机请求参数（JSON 字符串）
    /// </summary>
    private static string GenerateRandomParameters(Random random)
    {
        var parameters = new Dictionary<string, object>
        {
            ["taskId"]    = Guid.NewGuid().ToString("N"),
            ["index"]     = random.Next(1, 1_000_000),
            ["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            ["payload"]   = new { message = "hello from batch job", code = random.Next(1000, 9999) }
        };
        return JsonSerializer.Serialize(parameters);
    }

    /// <summary>
    /// 根据请求方法构造对应的 Echo 测试接口 URL
    /// GET  参数通过 query 传递，POST/PUT 通过 body 传递，DELETE 仅测试 Headers
    /// </summary>
    private static string BuildEchoUrl(string baseUrl, RequestTypeEnum requestType, string parametersJson)
    {
        return requestType switch
        {
            RequestTypeEnum.Get    => $"{baseUrl}/api/test/echo/query?payload={Uri.EscapeDataString(parametersJson)}",
            RequestTypeEnum.Post   => $"{baseUrl}/api/test/echo/body",
            RequestTypeEnum.Put    => $"{baseUrl}/api/test/echo/body",
            RequestTypeEnum.Delete => $"{baseUrl}/api/test/echo/headers",
            _                      => $"{baseUrl}/api/test/echo/headers"
        };
    }

    #endregion
}
