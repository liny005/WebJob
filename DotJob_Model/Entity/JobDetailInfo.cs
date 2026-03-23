
namespace DotJob_Model.Entity;

/// <summary>
/// 任务详情视图模型，用于查看/编辑任务时的数据传输。
/// 聚合了 JOB_CONFIG 配置、Quartz 触发器状态及 JOB_LOG 统计信息。
/// </summary>
public class JobDetailInfo
{
    /// <summary>
    /// 任务名称（不可修改）
    /// </summary>
    public string JobName { get; set; } = string.Empty;

    /// <summary>
    /// 任务分组（不可修改）
    /// </summary>
    public string JobGroup { get; set; } = string.Empty;

    /// <summary>
    /// 任务描述
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// 触发器类型：1=Cron，2=Simple（固定间隔）
    /// </summary>
    public int TriggerType { get; set; }

    /// <summary>
    /// Cron 表达式（TriggerType=1 时有效）
    /// </summary>
    public string Cron { get; set; } = string.Empty;

    /// <summary>
    /// 执行间隔秒数（TriggerType=2 时有效）
    /// </summary>
    public int IntervalSecond { get; set; }

    /// <summary>
    /// 触发器当前状态（0=None, 1=Normal, 2=Paused, 3=Complete, 4=Error, 5=Blocked）
    /// 使用 int 避免 DotJob_Model 项目依赖 Quartz
    /// </summary>
    public int TriggerState { get; set; }

    /// <summary>
    /// 请求地址
    /// </summary>
    public string RequestUrl { get; set; } = string.Empty;

    /// <summary>
    /// 请求类型：0=Get，1=Post，2=Put，3=Delete
    /// </summary>
    public int RequestType { get; set; }

    /// <summary>
    /// 请求头（JSON 格式）
    /// </summary>
    public string Headers { get; set; } = string.Empty;

    /// <summary>
    /// 请求参数（Post/Put Body）
    /// </summary>
    public string RequestParameters { get; set; } = string.Empty;

    /// <summary>
    /// 累计执行次数（来自 JOB_LOG 统计）
    /// </summary>
    public int RunNumber { get; set; }

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
    /// 下次执行时间（来自 Quartz 触发器）
    /// </summary>
    public DateTime? NextFireTime { get; set; }

    /// <summary>
    /// 上次执行时间（来自 Quartz 触发器）
    /// </summary>
    public DateTime? PreviousFireTime { get; set; }

    /// <summary>
    /// 邮件通知级别：0=不通知，1=仅失败通知，2=全部通知（暂未实现）
    /// </summary>
    public int MailMessage { get; set; }

    /// <summary>
    /// 钉钉通知级别：0=不通知，1=仅失败通知，2=全部通知
    /// </summary>
    public int Dingtalk { get; set; }
}

