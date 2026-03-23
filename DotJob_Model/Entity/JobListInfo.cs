namespace DotJob_Model.Entity;

/// <summary>
/// 任务列表视图模型，用于任务列表页展示。
/// 数据来源于 QRTZ_TRIGGERS 表和 JOB_CONFIG 表。
/// </summary>
public class JobListInfo
{
    /// <summary>任务名称</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>任务分组</summary>
    public string GroupName { get; set; } = string.Empty;

    /// <summary>
    /// 触发器状态（0=None, 1=Normal, 2=Paused, 3=Complete, 4=Error, 5=Blocked）
    /// </summary>
    public int TriggerState { get; set; }

    /// <summary>触发器类型：1=Cron，2=Simple（固定间隔）</summary>
    public int TriggerType { get; set; }

    /// <summary>Cron 表达式（TriggerType=1 时有效）</summary>
    public string? Cron { get; set; }

    /// <summary>执行间隔秒数（TriggerType=2 时有效）</summary>
    public int? IntervalSecond { get; set; }

    /// <summary>下次执行时间</summary>
    public DateTime? NextFireTime { get; set; }

    /// <summary>上次执行时间（来自 QRTZ_TRIGGERS.PREV_FIRE_TIME）</summary>
    public DateTime? PreviousFireTime { get; set; }
}

