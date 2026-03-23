using DotJob_Model;
using Job_Scheduler.Application.User;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Job_Scheduler.Controllers;

/// <summary>
/// 操作审计日志控制器
/// </summary>
[ApiController]
[Route("api/audit")]
[Authorize]
public class AuditLogController : ControllerBase
{
    private readonly AuditLogService _auditLogService;

    public AuditLogController(AuditLogService auditLogService)
    {
        _auditLogService = auditLogService;
    }

    /// <summary>
    /// 分页查询操作日志
    /// </summary>
    [HttpGet("logs")]
    public async Task<PageResponse<AuditLogResponse>> GetLogs(
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? operatorName = null,
        [FromQuery] string? action = null)
    {
        var (items, total) = await _auditLogService.QueryAsync(pageNumber, pageSize, operatorName, action);

        return new PageResponse<AuditLogResponse>
        {
            Data = items.Select(l => new AuditLogResponse
            {
                Id = l.Id,
                Operator = l.Operator,
                OperatorDisplayName = l.OperatorDisplayName,
                Action = l.Action,
                Target = l.Target,
                Remark = l.Remark,
                CreatedAt = l.CreatedAt
            }).ToList(),
            PageInfo = new PageInfo { PageNumber = pageNumber, PageSize = pageSize, Total = total }
        };
    }
}

/// <summary>
/// 审计日志响应
/// </summary>
public class AuditLogResponse
{
    public long Id { get; set; }
    public string Operator { get; set; } = string.Empty;
    public string? OperatorDisplayName { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? Target { get; set; }
    public string? Remark { get; set; }
    public DateTime CreatedAt { get; set; }
}

