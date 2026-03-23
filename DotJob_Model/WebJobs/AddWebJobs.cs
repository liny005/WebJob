using Host;
using Host.Common;
using Host.Common.Enums;

namespace DotJob_Model.WebJobs;

public class AddWebJobs
{
    /// <summary>
    /// 任务名称
    /// </summary>
    public string JobName { get; set; }

    /// <summary>
    /// 任务分组
    /// </summary>
    public string JobGroup { get; set; }

    /// <summary>
    /// 任务类型
    /// </summary>
    public JobTypeEnum JobType { get; set; } = JobTypeEnum.Url;

    /// <summary>
    /// 开始时间
    /// </summary>
    public DateTimeOffset BeginTime { get; set; } = DateTime.Now;

    /// <summary>
    /// 结束时间
    /// </summary>
    public DateTimeOffset? EndTime { get; set; }

    /// <summary>
    /// Cron表达式
    /// </summary>
    public string Cron { get; set; }

    /// <summary>
    /// 执行次数（默认无限循环）
    /// </summary>
    public int? RunTotal { get; set; }

    /// <summary>
    /// 执行间隔时间，单位秒（如果有Cron，则IntervalSecond失效）
    /// </summary>
    public int? IntervalSecond { get; set; }

    /// <summary>
    /// 触发器类型
    /// </summary>
    public TriggerTypeEnum TriggerType { get; set; }

    /// <summary>
    /// 描述
    /// </summary>
    public string Description { get; set; }

    /// <summary>
    /// 请求url
    /// </summary>
    public string RequestUrl { get; set; }

    /// <summary>
    /// 请求参数（Post，Put请求用）
    /// </summary>
    public string RequestParameters { get; set; }

    /// <summary>
    /// Headers(可以包含如：Authorization授权认证)
    /// 格式：{"Authorization":"userpassword.."}
    /// </summary>
    public string Headers { get; set; }

    /// <summary>
    /// 请求类型
    /// </summary>
    public RequestTypeEnum RequestType { get; set; } = RequestTypeEnum.Post;

    /// <summary>
    /// 是否通知邮件
    /// </summary>
    public int MailMessage { get; set; }

    /// <summary>
    /// 是否钉钉通知
    /// </summary>
    public int Dingtalk { get; set; }

    /// <summary>
    /// 执行次数
    /// </summary>
    public int RunNumber { get; set; } = 0;
}