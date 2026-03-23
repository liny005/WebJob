using DotJob_Model.Entity;
using MySql.Data.MySqlClient;

namespace Job_Scheduler.Application.User;

/// <summary>
/// 操作审计日志服务
/// </summary>
public class AuditLogService
{
    public AuditLogService() { }

    /// <summary>
    /// 记录一条操作日志
    /// </summary>
    public async Task LogAsync(string operatorUsername, string? operatorDisplayName, string action, string? target, string? remark = null)
    {
        await using var conn = new MySqlConnection(AppConfig.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"INSERT INTO JOB_AUDIT_LOG (OPERATOR, OPERATOR_DISPLAY_NAME, ACTION, TARGET, REMARK, CREATED_AT)
                            VALUES (@op, @opName, @action, @target, @remark, @now)";
        cmd.Parameters.AddWithValue("@op",     operatorUsername);
        cmd.Parameters.AddWithValue("@opName", (object?)operatorDisplayName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@action", action);
        cmd.Parameters.AddWithValue("@target", (object?)target ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@remark", (object?)remark ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@now",    DateTime.Now);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// 分页查询操作日志
    /// </summary>
    public async Task<(List<AuditLog> Items, int Total)> QueryAsync(
        int pageNumber, int pageSize,
        string? operatorName = null,
        string? action = null)
    {
        var where = new List<string>();
        if (!string.IsNullOrWhiteSpace(operatorName)) where.Add("OPERATOR LIKE @operatorName");
        if (!string.IsNullOrWhiteSpace(action))       where.Add("ACTION LIKE @action");
        var whereClause = where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : "";

        await using var conn = new MySqlConnection(AppConfig.ConnectionString);
        await conn.OpenAsync();

        // 查总数
        await using var countCmd = conn.CreateCommand();
        countCmd.CommandText = $"SELECT COUNT(*) FROM JOB_AUDIT_LOG {whereClause}";
        if (!string.IsNullOrWhiteSpace(operatorName)) countCmd.Parameters.AddWithValue("@operatorName", $"%{operatorName}%");
        if (!string.IsNullOrWhiteSpace(action))       countCmd.Parameters.AddWithValue("@action",       $"%{action}%");
        var total = Convert.ToInt32(await countCmd.ExecuteScalarAsync());

        // 分页查数据
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"SELECT ID, OPERATOR, OPERATOR_DISPLAY_NAME, ACTION, TARGET, REMARK, CREATED_AT
                             FROM JOB_AUDIT_LOG {whereClause}
                             ORDER BY CREATED_AT DESC
                             LIMIT @offset, @size";
        if (!string.IsNullOrWhiteSpace(operatorName)) cmd.Parameters.AddWithValue("@operatorName", $"%{operatorName}%");
        if (!string.IsNullOrWhiteSpace(action))       cmd.Parameters.AddWithValue("@action",       $"%{action}%");
        cmd.Parameters.AddWithValue("@offset", (pageNumber - 1) * pageSize);
        cmd.Parameters.AddWithValue("@size",   pageSize);

        await using var reader = await cmd.ExecuteReaderAsync();
        var items = new List<AuditLog>();
        while (await reader.ReadAsync())
        {
            items.Add(new AuditLog
            {
                Id                  = reader.GetInt64(0),
                Operator            = reader.GetString(1),
                OperatorDisplayName = reader.IsDBNull(2) ? null : reader.GetString(2),
                Action              = reader.GetString(3),
                Target              = reader.IsDBNull(4) ? null : reader.GetString(4),
                Remark              = reader.IsDBNull(5) ? null : reader.GetString(5),
                CreatedAt           = reader.GetDateTime(6)
            });
        }
        return (items, total);
    }
}
