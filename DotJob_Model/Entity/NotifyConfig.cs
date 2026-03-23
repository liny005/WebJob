namespace DotJob_Model.Entity;

/// <summary>
/// 推送通知配置实体，对应数据库表 JOB_NOTIFY_CONFIG_JSON。
/// 支持钉钉机器人等多种通知渠道，具体渠道配置以 JSON 格式存储于 Config 字段。
/// </summary>
public class NotifyConfig
{
    /// <summary>
    /// 主键 ID
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// 配置名称（如：钉钉机器人）
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 通知渠道标识（如：DingTalk、Feishu、Email）
    /// </summary>
    public string Channel { get; set; } = string.Empty;

    /// <summary>
    /// 渠道配置（JSON 格式）。
    /// 钉钉示例：{"webhookUrl":"https://oapi.dingtalk.com/robot/send?access_token=xxx","secret":"SECxxx"}
    /// </summary>
    public string Config { get; set; } = string.Empty;

    /// <summary>
    /// 是否启用
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// 最后更新时间
    /// </summary>
    public DateTime UpdatedAt { get; set; }
}

