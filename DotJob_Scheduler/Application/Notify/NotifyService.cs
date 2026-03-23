using DotJob_Model.Entity;
using MySql.Data.MySqlClient;

namespace Job_Scheduler.Application.Notify;

/// <summary>
/// 推送配置服务
/// 负责推送配置的 CRUD 操作，以及根据渠道类型将通知分发到对应的推送服务
/// </summary>
public class NotifyService
{
    private readonly DingTalkService _dingTalk;
    private readonly EmailService    _email;

    public NotifyService(DingTalkService dingTalk, EmailService email)
    {
        _dingTalk = dingTalk;
        _email    = email;
    }

    // ─── CRUD ────────────────────────────────────────────────────

    /// <summary>获取所有推送配置</summary>
    public async Task<List<NotifyConfig>> GetAllAsync()
    {
        await using var conn = new MySqlConnection(AppConfig.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT ID, NAME, CHANNEL, CONFIG, IS_ENABLED, CREATED_AT, UPDATED_AT FROM JOB_NOTIFY_CONFIG_JSON ORDER BY CREATED_AT";
        await using var reader = await cmd.ExecuteReaderAsync();
        var list = new List<NotifyConfig>();
        while (await reader.ReadAsync()) list.Add(MapConfig(reader));
        return list;
    }

    /// <summary>按 ID 获取单条推送配置</summary>
    public async Task<NotifyConfig?> GetByIdAsync(long id)
    {
        await using var conn = new MySqlConnection(AppConfig.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT ID, NAME, CHANNEL, CONFIG, IS_ENABLED, CREATED_AT, UPDATED_AT FROM JOB_NOTIFY_CONFIG_JSON WHERE ID = @id LIMIT 1";
        cmd.Parameters.AddWithValue("@id", id);
        await using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? MapConfig(reader) : null;
    }

    /// <summary>新增推送配置</summary>
    public async Task<NotifyConfig> CreateAsync(string name, string channel, string config)
    {
        var now = DateTime.Now;
        await using var conn = new MySqlConnection(AppConfig.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"INSERT INTO JOB_NOTIFY_CONFIG_JSON (NAME, CHANNEL, CONFIG, IS_ENABLED, CREATED_AT, UPDATED_AT)
                            VALUES (@name, @channel, @config, 1, @now, @now);
                            SELECT LAST_INSERT_ID();";
        cmd.Parameters.AddWithValue("@name",    name);
        cmd.Parameters.AddWithValue("@channel", channel);
        cmd.Parameters.AddWithValue("@config",  config);
        cmd.Parameters.AddWithValue("@now",     now);
        var newId = Convert.ToInt64(await cmd.ExecuteScalarAsync());
        return new NotifyConfig { Id = newId, Name = name, Channel = channel, Config = config, IsEnabled = true, CreatedAt = now, UpdatedAt = now };
    }

    /// <summary>更新推送配置</summary>
    public async Task UpdateAsync(long id, string name, string channel, string config, bool isEnabled)
    {
        await using var conn = new MySqlConnection(AppConfig.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"UPDATE JOB_NOTIFY_CONFIG_JSON
                            SET NAME=@name, CHANNEL=@channel, CONFIG=@config, IS_ENABLED=@isEnabled, UPDATED_AT=@now
                            WHERE ID=@id";
        cmd.Parameters.AddWithValue("@name",      name);
        cmd.Parameters.AddWithValue("@channel",   channel);
        cmd.Parameters.AddWithValue("@config",    config);
        cmd.Parameters.AddWithValue("@isEnabled", isEnabled ? 1 : 0);
        cmd.Parameters.AddWithValue("@now",       DateTime.Now);
        cmd.Parameters.AddWithValue("@id",        id);
        var affected = await cmd.ExecuteNonQueryAsync();
        if (affected == 0) throw new InvalidOperationException("配置不存在");
    }

    /// <summary>删除推送配置</summary>
    public async Task DeleteAsync(long id)
    {
        await using var conn = new MySqlConnection(AppConfig.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM JOB_NOTIFY_CONFIG_JSON WHERE ID = @id";
        cmd.Parameters.AddWithValue("@id", id);
        var affected = await cmd.ExecuteNonQueryAsync();
        if (affected == 0) throw new InvalidOperationException("配置不存在");
    }

    // ─── 分发推送 ─────────────────────────────────────────────────

    /// <summary>
    /// 向所有已启用的配置发送推送通知
    /// </summary>
    public async Task SendToAllEnabledAsync(string jobName, string requestUrl, string result, bool isSuccess)
    {
        await using var conn = new MySqlConnection(AppConfig.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT ID, NAME, CHANNEL, CONFIG, IS_ENABLED, CREATED_AT, UPDATED_AT FROM JOB_NOTIFY_CONFIG_JSON WHERE IS_ENABLED = 1";
        await using var reader = await cmd.ExecuteReaderAsync();
        var configs = new List<NotifyConfig>();
        while (await reader.ReadAsync()) configs.Add(MapConfig(reader));

        foreach (var config in configs)
        {
            try   { await SendAsync(config, jobName, requestUrl, result, isSuccess); }
            catch (Exception ex)
            {
                Console.WriteLine($"[Notify] 推送失败 [{config.Channel}][{config.Name}]: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// 向指定配置发送推送通知，根据渠道类型路由到对应服务
    /// </summary>
    public async Task SendAsync(NotifyConfig config, string jobName, string requestUrl, string result, bool isSuccess)
    {
        switch (config.Channel)
        {
            case "DingTalk":
                await _dingTalk.SendAsync(config, jobName, requestUrl, result, isSuccess);
                break;
            case "Email":
                await _email.SendAsync(config, jobName, requestUrl, result, isSuccess);
                break;
            default:
                Console.WriteLine($"[Notify] 不支持的渠道类型: {config.Channel}");
                break;
        }
    }

    /// <summary>
    /// 测试指定配置的推送是否正常
    /// </summary>
    public async Task TestAsync(long id)
    {
        var config = await GetByIdAsync(id) ?? throw new InvalidOperationException("配置不存在");
        var testResult = config.Channel == "Email"
            ? "这是一封来自 DotJob 调度平台的测试邮件，说明您的邮件推送配置已生效。"
            : "{\"Success\":true,\"Msg\":\"测试消息\"}";
        await SendAsync(config, "测试任务", "https://test.example.com", testResult, true);
    }

    // ─── 私有工具 ─────────────────────────────────────────────────

    private static NotifyConfig MapConfig(System.Data.Common.DbDataReader reader) => new()
    {
        Id        = reader.GetInt64(0),
        Name      = reader.GetString(1),
        Channel   = reader.GetString(2),
        Config    = reader.GetString(3),
        IsEnabled = reader.GetBoolean(4),
        CreatedAt = reader.GetDateTime(5),
        UpdatedAt = reader.GetDateTime(6)
    };
}

