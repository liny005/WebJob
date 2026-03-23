using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Web;
using DotJob_Model.Entity;

namespace Job_Scheduler.Application.Notify;

/// <summary>
/// 钉钉机器人推送服务
/// 负责将任务执行结果以 Markdown 消息格式推送到钉钉群机器人
/// </summary>
public class DingTalkService
{
    private static readonly HttpClient _http = new();

    /// <summary>
    /// 发送钉钉推送通知
    /// Config JSON 格式：{"webhookUrl":"https://oapi.dingtalk.com/robot/send?access_token=xxx","secret":"SECxxx"}
    /// </summary>
    /// <param name="config">推送配置实体</param>
    /// <param name="jobName">任务名称</param>
    /// <param name="requestUrl">任务请求地址</param>
    /// <param name="result">执行结果内容</param>
    /// <param name="isSuccess">是否执行成功</param>
    public async Task SendAsync(NotifyConfig config, string jobName, string requestUrl, string result, bool isSuccess)
    {
        var cfg = JsonSerializer.Deserialize<JsonElement>(config.Config);
        var webhookUrl = cfg.TryGetProperty("webhookUrl", out var wu) ? wu.GetString() : null;
        var secret     = cfg.TryGetProperty("secret",     out var s)  ? s.GetString()  : null;

        if (string.IsNullOrWhiteSpace(webhookUrl))
            throw new InvalidOperationException("钉钉 WebhookUrl 未配置");

        var statusColor = isSuccess ? "LightSeaGreen" : "red";
        var statusText  = isSuccess ? "成功" : "失败";
        var text = $"## 调度通知  \n" +
                   $"- **任务名称：** {jobName}  \n" +
                   $"- **触发地址：** {requestUrl}  \n" +
                   $"- **触发时间：** {DateTime.Now:yyyy-MM-dd HH:mm:ss}  \n" +
                   $"- **执行结果：** <font color={statusColor}>**{statusText}**</font>  \n" +
                   $"- **返回内容：** {result}";

        var body = JsonSerializer.Serialize(new
        {
            msgtype  = "markdown",
            markdown = new { title = "调度通知", text }
        });

        var url = string.IsNullOrWhiteSpace(secret)
            ? webhookUrl
            : $"{webhookUrl}&{BuildSignature(secret)}";

        var content = new StringContent(body, Encoding.UTF8, "application/json");
        await _http.PostAsync(url, content);
    }

    /// <summary>
    /// 构造钉钉签名参数（timestamp + HMAC-SHA256）
    /// </summary>
    /// <param name="secret">加签密钥</param>
    private static string BuildSignature(string secret)
    {
        var timestamp     = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var stringToSign  = $"{timestamp}\n{secret}";
        using var hmac    = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var sign          = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(stringToSign)));
        return $"timestamp={timestamp}&sign={HttpUtility.UrlEncode(sign)}";
    }
}