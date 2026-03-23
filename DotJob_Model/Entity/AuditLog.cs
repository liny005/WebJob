namespace DotJob_Model.Entity;

/// <summary>
/// 操作审计日志实体，对应数据库表 JOB_AUDIT_LOG。
/// 记录系统中所有重要操作，包括新增/修改/删除任务、手动执行、用户管理、登录等。
/// </summary>
public class AuditLog
{
    /// <summary>
    /// 主键 ID
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// 操作人用户名
    /// </summary>
    public string Operator { get; set; } = string.Empty;

    /// <summary>
    /// 操作人显示名称（昵称）
    /// </summary>
    public string? OperatorDisplayName { get; set; }

    /// <summary>
    /// 操作类型（如：新增任务、修改任务、删除任务、立即执行、暂停任务、恢复任务、登录、新增用户、删除用户等）
    /// </summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>
    /// 操作目标（如：任务全名、用户名等）
    /// </summary>
    public string? Target { get; set; }

    /// <summary>
    /// 备注信息（如：修改任务时记录请求参数的 JSON）
    /// </summary>
    public string? Remark { get; set; }

    /// <summary>
    /// 操作时间
    /// </summary>
    public DateTime CreatedAt { get; set; }
}

