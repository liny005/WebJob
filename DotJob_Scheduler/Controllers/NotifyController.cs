using System.Security.Claims;
using System.Text.Json;
using System.ComponentModel.DataAnnotations;
using Job_Scheduler.Application.Notify;
using Job_Scheduler.Application.User;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Job_Scheduler.Controllers;

[ApiController]
[Route("api/notify")]
[Authorize(Roles = "Admin")]
public class NotifyController : ControllerBase
{
    private readonly NotifyService   _notifyService;
    private readonly AuditLogService _auditLogService;

    public NotifyController(NotifyService notifyService, AuditLogService auditLogService)
    {
        _notifyService   = notifyService;
        _auditLogService = auditLogService;
    }

    // 获取当前操作人信息
    private (string username, string? displayName) GetOperator()
    {
        var username = User.FindFirstValue(ClaimTypes.Name) ?? "unknown";
        var displayName = User.FindFirstValue("DisplayName");
        return (username, displayName);
    }

    /// <summary>获取所有推送配置</summary>
    [HttpGet("list")]
    public async Task<object> GetListAsync()
    {
        var list = await _notifyService.GetAllAsync();
        return list.Select(c => new
        {
            c.Id, c.Name, c.Channel, c.Config, c.IsEnabled, c.CreatedAt, c.UpdatedAt
        });
    }

    /// <summary>新增推送配置</summary>
    [HttpPost("create")]
    public async Task CreateAsync([FromBody] NotifyConfigRequest request)
    {
        try { JsonSerializer.Deserialize<JsonElement>(request.Config ?? "{}"); }
        catch { throw new Exception("Config 必须是合法的 JSON 格式"); }

        await _notifyService.CreateAsync(request.Name, request.Channel, request.Config ?? "{}");

        var (username, displayName) = GetOperator();
        await _auditLogService.LogAsync(username, displayName,
            "新增推送配置",
            $"渠道: {request.Channel}, 名称: {request.Name}");
    }

    /// <summary>更新推送配置</summary>
    [HttpPut("{id}")]
    public async Task UpdateAsync(long id, [FromBody] NotifyConfigRequest request)
    {
        try { JsonSerializer.Deserialize<JsonElement>(request.Config ?? "{}"); }
        catch { throw new Exception("Config 必须是合法的 JSON 格式"); }

        await _notifyService.UpdateAsync(id, request.Name, request.Channel ?? "DingTalk", request.Config ?? "{}", request.IsEnabled);

        var (username, displayName) = GetOperator();
        // 格式化 JSON 便于阅读
        string? remark = null;
        try
        {
            var parsed = JsonSerializer.Deserialize<object>(request.Config ?? "{}");
            remark = JsonSerializer.Serialize(parsed, new JsonSerializerOptions { WriteIndented = true });
        }
        catch { remark = request.Config; }

        await _auditLogService.LogAsync(username, displayName,
            "修改推送配置",
            $"渠道: {request.Channel}, 名称: {request.Name}",
            remark: remark);
    }

    /// <summary>删除推送配置</summary>
    [HttpDelete("{id}")]
    public async Task DeleteAsync(long id)
    {
        var config = await _notifyService.GetByIdAsync(id);
        await _notifyService.DeleteAsync(id);

        var (username, displayName) = GetOperator();
        await _auditLogService.LogAsync(username, displayName,
            "删除推送配置",
            $"渠道: {config?.Channel}, 名称: {config?.Name}");
    }

    /// <summary>测试推送</summary>
    [HttpPost("{id}/test")]
    public async Task TestAsync(long id)
    {
        var config = await _notifyService.GetByIdAsync(id);
        await _notifyService.TestAsync(id);

        var (username, displayName) = GetOperator();
        await _auditLogService.LogAsync(username, displayName,
            "测试推送配置",
            $"渠道: {config?.Channel}, 名称: {config?.Name}");
    }

}

public class NotifyConfigRequest
{
    [Required(ErrorMessage = "配置名称不能为空")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "渠道类型不能为空")]
    public string Channel { get; set; } = "DingTalk";

    public string? Config { get; set; }
    public bool IsEnabled { get; set; } = true;
}
