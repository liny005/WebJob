namespace DotJob_Model.Auth;

/// <summary>
/// 登录响应
/// </summary>
public class LoginResponse
{
    /// <summary>
    /// 用户ID
    /// </summary>
    public long UserId { get; set; }

    /// <summary>
    /// 用户名
    /// </summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// 显示名称
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// JWT Token (可选)
    /// </summary>
    public string? Token { get; set; }

    /// <summary>
    /// 角色: Admin, User
    /// </summary>
    public string Role { get; set; } = "User";
}
