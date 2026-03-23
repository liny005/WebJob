namespace DotJob_Model.Entity;

/// <summary>
/// 任务配置实体，对应数据库表 JOB_CONFIG。
/// 替代原来存储在 Quartz JobDataMap 中的业务数据，Quartz 只负责触发，所有扩展配置均存于此表。
/// </summary>
public class JobConfig
{
    /// <summary>
    /// 任务名称（与 Quartz JobKey.Name 对应）
    /// </summary>
    public string JobName { get; set; } = string.Empty;

    /// <summary>
    /// 任务分组（与 Quartz JobKey.Group 对应）
    /// </summary>
    public string JobGroup { get; set; } = string.Empty;

    /// <summary>
    /// 任务描述
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// 请求地址
    /// </summary>
    public string RequestUrl { get; set; } = string.Empty;

    /// <summary>
    /// 请求类型：0=Get，1=Post，2=Put，3=Delete
    /// </summary>
    public int RequestType { get; set; }

    /// <summary>
    /// 请求头（JSON 格式，如 {"Authorization":"Bearer xxx"}）
    /// </summary>
    public string? Headers { get; set; }

    /// <summary>
    /// 请求参数（Post/Put 的 Body 内容）
    /// </summary>
    public string? RequestParameters { get; set; }

    /// <summary>
    /// 触发器类型：1=Cron，2=Simple（固定间隔）
    /// </summary>
    public int TriggerType { get; set; }

    /// <summary>
    /// Cron 表达式（TriggerType=1 时有效）
    /// </summary>
    public string? Cron { get; set; }

    /// <summary>
    /// 执行间隔秒数（TriggerType=2 时有效）
    /// </summary>
    public int? IntervalSecond { get; set; }

    /// <summary>
    /// 任务开始时间
    /// </summary>
    public DateTime? BeginTime { get; set; }

    /// <summary>
    /// 任务结束时间（到达后自动暂停）
    /// </summary>
    public DateTime? EndTime { get; set; }

    /// <summary>
    /// 执行次数上限（达到后自动暂停，null 表示无限）
    /// </summary>
    public int? RunTotal { get; set; }

    /// <summary>
    /// 钉钉通知级别：0=不通知，1=仅失败通知，2=全部通知
    /// </summary>
    public int Dingtalk { get; set; }

    /// <summary>
    /// 邮件通知级别：0=不通知，1=仅失败通知，2=全部通知（暂未实现）
    /// </summary>
    public int MailMessage { get; set; }

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// 最后更新时间
    /// </summary>
    public DateTime UpdatedAt { get; set; }
}

