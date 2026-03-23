namespace DotJob_Model.Entity;

/// <summary>
/// 任务执行日志实体，对应数据库表 JOB_LOG
/// </summary>
public class LogEntity
{
    /// <summary>
    /// 主键 ID
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// 执行开始时间
    /// </summary>
    public DateTime BeginTime { get; set; }

    /// <summary>
    /// 执行结束时间
    /// </summary>
    public DateTime EndTime { get; set; }

    /// <summary>
    /// 任务全名（格式：JobGroup.JobName）
    /// </summary>
    public string? JobName { get; set; }

    /// <summary>
    /// 任务分组
    /// </summary>
    public string? JobGroup { get; set; }

    /// <summary>
    /// 错误信息（执行失败时记录）
    /// </summary>
    public string? ErrorMsg { get; set; }

    /// <summary>
    /// 执行耗时（单位：秒）
    /// </summary>
    public double ExecuteTime { get; set; }

    /// <summary>
    /// 执行状态：1=成功，2=失败
    /// </summary>
    public int ExecutionStatus { get; set; }

    /// <summary>
    /// 请求地址
    /// </summary>
    public string? Url { get; set; }

    /// <summary>
    /// 请求类型（Get / Post / Put / Delete）
    /// </summary>
    public string? RequestType { get; set; }

    /// <summary>
    /// 请求参数
    /// </summary>
    public string? Parameters { get; set; }

    /// <summary>
    /// 接口返回结果
    /// </summary>
    public string? Result { get; set; }

    /// <summary>
    /// HTTP 响应状态码
    /// </summary>
    public int? StatusCode { get; set; }
}

