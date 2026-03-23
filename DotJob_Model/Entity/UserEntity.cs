namespace DotJob_Model.Entity;

/// <summary>
/// 系统用户实体，对应数据库表 JOB_USER
/// </summary>
public class UserEntity
{
    /// <summary>
    /// 主键 ID
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// 登录用户名（唯一）
    /// </summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// 密码（SHA256 哈希存储）
    /// </summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// 显示名称（昵称）
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// 邮箱地址
    /// </summary>
    public string? Email { get; set; }

    /// <summary>
    /// 是否启用
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// 角色：Admin=管理员，User=普通用户
    /// </summary>
    public string Role { get; set; } = "User";

    /// <summary>
    /// 账号创建时间
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// 最后登录时间
    /// </summary>
    public DateTime? LastLoginAt { get; set; }
}

