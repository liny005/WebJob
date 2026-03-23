using System.Text.Json;
using DotJob_Model.Entity;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace Job_Scheduler.Application.Notify;

/// <summary>
/// 邮件推送服务
/// 通过 SMTP 协议发送 HTML 格式的任务执行结果通知邮件
/// </summary>
public class EmailService
{
    /// <summary>
    /// 发送邮件推送通知
    /// Config JSON 格式：
    /// {
    ///   "smtpHost":  "smtp.163.com",
    ///   "smtpPort":  465,
    ///   "useSsl":    true,
    ///   "username":  "sender@163.com",
    ///   "password":  "授权码",
    ///   "fromName":  "DotJob 调度系统",
    ///   "to":        "a@example.com,b@example.com"
    /// }
    /// </summary>
    /// <param name="config">推送配置实体</param>
    /// <param name="jobName">任务名称</param>
    /// <param name="requestUrl">任务请求地址</param>
    /// <param name="result">执行结果内容</param>
    /// <param name="isSuccess">是否执行成功</param>
    public async Task SendAsync(NotifyConfig config, string jobName, string requestUrl, string result, bool isSuccess)
    {
        var cfg = JsonSerializer.Deserialize<JsonElement>(config.Config);

        // 读取配置字段的辅助方法
        string Str(string key)            => cfg.TryGetProperty(key, out var v) ? v.GetString() ?? "" : "";
        int    Int(string key, int def)   => cfg.TryGetProperty(key, out var v) && v.TryGetInt32(out var i) ? i : def;
        bool   Bool(string key, bool def) => cfg.TryGetProperty(key, out var v)
                                               ? v.ValueKind == JsonValueKind.True
                                               : def;

        var smtpHost = Str("smtpHost");
        var smtpPort = Int("smtpPort", 465);
        var useSsl   = Bool("useSsl", true);
        var username = Str("username");
        var password = Str("password");
        var fromName = Str("fromName");
        var toRaw    = Str("to");

        if (string.IsNullOrWhiteSpace(smtpHost))
            throw new InvalidOperationException("邮件配置缺少 smtpHost");
        if (string.IsNullOrWhiteSpace(username))
            throw new InvalidOperationException("邮件配置缺少 username（发件人账号）");
        if (string.IsNullOrWhiteSpace(toRaw))
            throw new InvalidOperationException("邮件配置缺少收件人 to");

        // 支持逗号分隔的多个收件人
        var toAddresses = toRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var statusText  = isSuccess ? "✅ 成功" : "❌ 失败";
        var statusColor = isSuccess ? "#28a745" : "#dc3545";
        var now         = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

        // 构建 HTML 邮件正文
        var htmlBody = $@"
<html>
<body style='font-family:Arial,sans-serif;background:#f5f5f5;padding:20px;margin:0;'>
  <div style='max-width:600px;margin:0 auto;background:#fff;border-radius:8px;padding:30px;box-shadow:0 2px 8px rgba(0,0,0,.1)'>
    <h2 style='color:#333;margin-top:0;border-bottom:2px solid #f0f0f0;padding-bottom:12px'>
      📅 DotJob 调度通知
    </h2>
    <table style='width:100%;border-collapse:collapse'>
      <tr style='background:#f8f9fa'>
        <td style='padding:10px 12px;color:#666;width:120px;white-space:nowrap'>任务名称</td>
        <td style='padding:10px 12px;font-weight:bold'>{System.Net.WebUtility.HtmlEncode(jobName)}</td>
      </tr>
      <tr>
        <td style='padding:10px 12px;color:#666'>触发地址</td>
        <td style='padding:10px 12px;word-break:break-all'>{System.Net.WebUtility.HtmlEncode(requestUrl)}</td>
      </tr>
      <tr style='background:#f8f9fa'>
        <td style='padding:10px 12px;color:#666'>触发时间</td>
        <td style='padding:10px 12px'>{now}</td>
      </tr>
      <tr>
        <td style='padding:10px 12px;color:#666'>执行结果</td>
        <td style='padding:10px 12px;font-weight:bold;color:{statusColor}'>{statusText}</td>
      </tr>
      <tr style='background:#f8f9fa'>
        <td style='padding:10px 12px;color:#666;vertical-align:top'>返回内容</td>
        <td style='padding:10px 12px;word-break:break-all;white-space:pre-wrap;font-size:0.9em'>{System.Net.WebUtility.HtmlEncode(result)}</td>
      </tr>
    </table>
    <p style='color:#bbb;font-size:12px;margin-bottom:0;margin-top:20px;border-top:1px solid #f0f0f0;padding-top:12px'>
      此邮件由 DotJob 调度平台自动发送，请勿直接回复。
    </p>
  </div>
</body>
</html>";

        // 构建 MimeMessage
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(
            string.IsNullOrWhiteSpace(fromName) ? "DotJob 调度系统" : fromName,
            username));
        foreach (var addr in toAddresses)
            message.To.Add(MailboxAddress.Parse(addr));

        message.Subject = $"【DotJob】{jobName} 执行{(isSuccess ? "成功" : "失败")} - {now}";
        message.Body    = new TextPart("html") { Text = htmlBody };

        // 发送
        using var client = new SmtpClient();
        var socketOption = useSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTlsWhenAvailable;
        await client.ConnectAsync(smtpHost, smtpPort, socketOption);
        await client.AuthenticateAsync(username, password);
        await client.SendAsync(message);
        await client.DisconnectAsync(true);
    }
}

