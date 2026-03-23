using DotJob_Model;
using Job_Scheduler.Application.User;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;

namespace Job_Scheduler.Controllers;

/// <summary>
/// 用户管理控制器 仅 Admin 可访问
/// </summary>
[ApiController]
[Route("api/user")]
[Authorize(Roles = "Admin")]
public class UserController : ControllerBase
{
    private readonly AuthService _authService;
    private readonly AuditLogService _auditLogService;

    public UserController(AuthService authService, AuditLogService auditLogService)
    {
        _authService     = authService;
        _auditLogService = auditLogService;
    }

    /// <summary>
    /// 分页获取用户列表 每页20条
    /// </summary>
    /// <param name="pageNumber">页码 从1开始 默认1</param>
    /// <param name="pageSize">每页数量 默认20</param>
    [HttpGet("list")]
    public async Task<PageResponse<UserInfoResponse>> GetUsers(
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize   = 20)
    {
        pageNumber = Math.Max(1, pageNumber);
        pageSize   = Math.Clamp(pageSize, 1, 100);

        var (items, total) = await _authService.GetUsersPagedAsync(pageNumber, pageSize);

        return new PageResponse<UserInfoResponse>
        {
            Data = items.Select(u => new UserInfoResponse
            {
                Id          = u.Id,
                Username    = u.Username,
                DisplayName = u.DisplayName,
                Email       = u.Email,
                Role        = u.Role,
                IsEnabled   = u.IsEnabled,
                CreatedAt   = u.CreatedAt,
                LastLoginAt = u.LastLoginAt
            }).ToList(),
            PageInfo = new PageInfo
            {
                Total      = total,
                PageNumber = pageNumber,
                PageSize   = pageSize
            }
        };
    }

    /// <summary>
    /// 新增用户
    /// </summary>
    [HttpPost("create")]
    public async Task<UserInfoResponse> CreateUser([FromBody] CreateUserRequest request)
    {
        var user = await _authService.CreateUserAsync(
            request.Username,
            request.Password,
            request.DisplayName,
            request.Email,
            request.Role ?? "User");

        var operatorName = User.Identity?.Name ?? "unknown";
        var displayName  = User.FindFirst("DisplayName")?.Value;
        await _auditLogService.LogAsync(operatorName, displayName, "新增用户", $"用户名: {request.Username}");

        return new UserInfoResponse
        {
            Id          = user.Id,
            Username    = user.Username,
            DisplayName = user.DisplayName,
            Email       = user.Email,
            Role        = user.Role,
            IsEnabled   = user.IsEnabled,
            CreatedAt   = user.CreatedAt
        };
    }

    /// <summary>
    /// 删除用户
    /// </summary>
    [HttpDelete("{userId}")]
    public async Task DeleteUser(long userId)
    {
        var operatorIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!long.TryParse(operatorIdStr, out var operatorId))
            throw new UnauthorizedAccessException("无效的操作人ID");

        var targetUser = await _authService.GetUserByIdAsync(userId);
        await _authService.DeleteUserAsync(userId, operatorId);

        var operatorName = User.Identity?.Name ?? "unknown";
        var displayName  = User.FindFirst("DisplayName")?.Value;
        await _auditLogService.LogAsync(operatorName, displayName, "删除用户", $"用户名: {targetUser?.Username}");
    }
}

/// <summary>
/// 用户信息响应
/// </summary>
public class UserInfoResponse
{
    public long      Id          { get; set; }
    public string    Username    { get; set; } = string.Empty;
    public string?   DisplayName { get; set; }
    public string?   Email       { get; set; }
    public string    Role        { get; set; } = "User";
    public bool      IsEnabled   { get; set; }
    public DateTime  CreatedAt   { get; set; }
    public DateTime? LastLoginAt { get; set; }
}

/// <summary>
/// 新增用户请求
/// </summary>
public class CreateUserRequest
{
    [Required(ErrorMessage = "用户名不能为空")]
    public string  Username    { get; set; } = string.Empty;

    [Required(ErrorMessage = "密码不能为空")]
    public string  Password    { get; set; } = string.Empty;

    public string? DisplayName { get; set; }
    public string? Email       { get; set; }
    public string? Role        { get; set; }
}
