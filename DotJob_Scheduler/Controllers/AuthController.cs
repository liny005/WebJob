using System;
using System.Collections.Generic;
using DotJob_Model.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Threading.Tasks;
using Job_Scheduler.Application.User;
using Microsoft.AspNetCore.Authorization;

namespace Job_Scheduler.Controllers;

/// <summary>
/// 认证控制器
/// </summary>
[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly AuthService _authService;
    private readonly AuditLogService _auditLogService;

    public AuthController(AuthService authService, AuditLogService auditLogService)
    {
        _authService = authService;
        _auditLogService = auditLogService;
    }

    /// <summary>
    /// 用户登录
    /// </summary>
    [HttpPost("login")]
    public async Task<LoginResponse> Login([FromBody] LoginRequest request)
    {
        var result = await _authService.LoginAsync(request);

        // 创建 Claims
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, result.UserId.ToString()),
            new(ClaimTypes.Name, result.Username),
            new(ClaimTypes.Role, result.Role),
        };

        if (!string.IsNullOrEmpty(result.DisplayName))
        {
            claims.Add(new Claim("DisplayName", result.DisplayName));
        }

        var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var authProperties = new AuthenticationProperties
        {
            IsPersistent = request.RememberMe,
            ExpiresUtc = request.RememberMe ? DateTimeOffset.UtcNow.AddDays(7) : DateTimeOffset.UtcNow.AddHours(8)
        };

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(claimsIdentity),
            authProperties);

        // 记录登录操作日志
        await _auditLogService.LogAsync(result.Username, result.DisplayName, "用户登录", null);

        return result;
    }

    /// <summary>
    /// 用户登出
    /// </summary>
    [HttpPost("logout")]
    public async Task<LogoutResponse> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return new LogoutResponse { Success = true, Message = "登出成功" };
    }

    /// <summary>
    /// 获取当前用户信息
    /// </summary>
    [HttpGet("current")]
    public CurrentUserResponse GetCurrentUser()
    {
        if (!User.Identity?.IsAuthenticated ?? true)
        {
            return new CurrentUserResponse
            {
                UserId = null,
                Username = null
            };
        }

        return new CurrentUserResponse
        {
            UserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value,
            Username = User.Identity.Name,
            DisplayName = User.FindFirst("DisplayName")?.Value ?? User.Identity.Name,
            Role = User.FindFirst(ClaimTypes.Role)?.Value
        };
    }

    /// <summary>
    /// 修改密码
    /// </summary>
    [HttpPost("change-password"), Authorize]
    public async Task<ChangePasswordResponse> ChangePassword([FromBody] ChangePasswordRequest request)
    {
        var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!long.TryParse(userIdStr, out var userId))
            throw new UnauthorizedAccessException("无效的用户ID");

        var result = await _authService.ChangePasswordAsync(userId, request.OldPassword, request.NewPassword);
        if (!result)
            throw new InvalidOperationException("原密码错误");

        return new ChangePasswordResponse { Success = true, Message = "密码修改成功" };
    }
}

/// <summary>
/// 修改密码请求
/// </summary>
public class ChangePasswordRequest
{
    public string OldPassword { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
}

/// <summary>
/// 修改密码响应
/// </summary>
public class ChangePasswordResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// 登出响应
/// </summary>
public class LogoutResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// 当前用户响应
/// </summary>
public class CurrentUserResponse
{
    public string? UserId { get; set; }
    public string? Username { get; set; }
    public string? DisplayName { get; set; }
    public string? Role { get; set; }
}