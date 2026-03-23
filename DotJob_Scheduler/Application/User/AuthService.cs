using System.Security.Cryptography;
using System.Text;
using DotJob_Model.Entity;
using DotJob_Model.Auth;
using MySql.Data.MySqlClient;

namespace Job_Scheduler.Application.User;

/// <summary>
/// 认证服务
/// </summary>
public class AuthService
{
    /// <summary>
    /// 用户登录
    /// </summary>
    public async Task<LoginResponse> LoginAsync(LoginRequest request)
    {
        await using var conn = new MySqlConnection(AppConfig.ConnectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT ID, USERNAME, PASSWORD, DISPLAY_NAME, ROLE, IS_ENABLED FROM JOB_USER WHERE USERNAME = @username LIMIT 1";
        cmd.Parameters.AddWithValue("@username", request.Username);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            throw new UnauthorizedAccessException("用户名或密码错误");

        var id = reader.GetInt64(0);
        var username = reader.GetString(1);
        var storedHash = reader.GetString(2);
        var displayName = reader.IsDBNull(3) ? null : reader.GetString(3);
        var role = reader.GetString(4);
        var isEnabled = reader.GetBoolean(5);
        await reader.CloseAsync();

        if (storedHash != HashPassword(request.Password))
            throw new UnauthorizedAccessException("用户名或密码错误");

        if (!isEnabled)
            throw new UnauthorizedAccessException("用户已被禁用");

        // 更新最后登录时间
        await using var updateCmd = conn.CreateCommand();
        updateCmd.CommandText = "UPDATE JOB_USER SET LAST_LOGIN_AT = @now WHERE ID = @id";
        updateCmd.Parameters.AddWithValue("@now", DateTime.Now);
        updateCmd.Parameters.AddWithValue("@id", id);
        await updateCmd.ExecuteNonQueryAsync();

        return new LoginResponse
        {
            UserId = id,
            Username = username,
            DisplayName = displayName ?? username,
            Role = role
        };
    }

    /// <summary>
    /// 获取用户信息
    /// </summary>
    public async Task<UserEntity?> GetUserByIdAsync(long userId)
    {
        await using var conn = new MySqlConnection(AppConfig.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT ID, USERNAME, PASSWORD, DISPLAY_NAME, EMAIL, IS_ENABLED, ROLE, CREATED_AT, LAST_LOGIN_AT FROM JOB_USER WHERE ID = @id LIMIT 1";
        cmd.Parameters.AddWithValue("@id", userId);
        await using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? MapUser(reader) : null;
    }

    /// <summary>
    /// 分页获取用户列表
    /// </summary>
    /// <param name="pageNumber">页码 从1开始</param>
    /// <param name="pageSize">每页数量 默认20</param>
    /// <returns>用户列表与总数</returns>
    public async Task<(List<UserEntity> Items, int Total)> GetUsersPagedAsync(int pageNumber = 1, int pageSize = 20)
    {
        await using var conn = new MySqlConnection(AppConfig.ConnectionString);
        await conn.OpenAsync();

        // 查总数
        await using var countCmd = conn.CreateCommand();
        countCmd.CommandText = "SELECT COUNT(*) FROM JOB_USER";
        var total = Convert.ToInt32(await countCmd.ExecuteScalarAsync());

        // 分页查数据
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT ID, USERNAME, PASSWORD, DISPLAY_NAME, EMAIL, IS_ENABLED, ROLE, CREATED_AT, LAST_LOGIN_AT
                            FROM JOB_USER
                            ORDER BY CREATED_AT
                            LIMIT @offset, @size";
        cmd.Parameters.AddWithValue("@offset", (pageNumber - 1) * pageSize);
        cmd.Parameters.AddWithValue("@size", pageSize);

        await using var reader = await cmd.ExecuteReaderAsync();
        var list = new List<UserEntity>();
        while (await reader.ReadAsync()) list.Add(MapUser(reader));

        return (list, total);
    }

    /// <summary>
    /// 新增用户
    /// </summary>
    public async Task<UserEntity> CreateUserAsync(string username, string password, string? displayName, string? email, string role = "User")
    {
        await using var conn = new MySqlConnection(AppConfig.ConnectionString);
        await conn.OpenAsync();

        // 检查用户名是否已存在
        await using var checkCmd = conn.CreateCommand();
        checkCmd.CommandText = "SELECT COUNT(*) FROM JOB_USER WHERE USERNAME = @username";
        checkCmd.Parameters.AddWithValue("@username", username);
        var count = Convert.ToInt32(await checkCmd.ExecuteScalarAsync());
        if (count > 0) throw new InvalidOperationException($"用户名 '{username}' 已存在");

        var now = DateTime.Now;
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"INSERT INTO JOB_USER (USERNAME, PASSWORD, DISPLAY_NAME, EMAIL, ROLE, IS_ENABLED, CREATED_AT)
                            VALUES (@username, @password, @displayName, @email, @role, 1, @createdAt);
                            SELECT LAST_INSERT_ID();";
        cmd.Parameters.AddWithValue("@username", username);
        cmd.Parameters.AddWithValue("@password", HashPassword(password));
        cmd.Parameters.AddWithValue("@displayName", (object?)displayName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@email", (object?)email ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@role", role);
        cmd.Parameters.AddWithValue("@createdAt", now);
        var newId = Convert.ToInt64(await cmd.ExecuteScalarAsync());

        return new UserEntity
        {
            Id = newId, Username = username, DisplayName = displayName,
            Email = email, Role = role, IsEnabled = true, CreatedAt = now
        };
    }

    /// <summary>
    /// 删除用户 仅 Admin 可调用，且不能删除自己
    /// </summary>
    public async Task DeleteUserAsync(long userId, long operatorId)
    {
        if (userId == operatorId) throw new InvalidOperationException("不能删除自己");

        await using var conn = new MySqlConnection(AppConfig.ConnectionString);
        await conn.OpenAsync();

        await using var checkCmd = conn.CreateCommand();
        checkCmd.CommandText = "SELECT USERNAME FROM JOB_USER WHERE ID = @id LIMIT 1";
        checkCmd.Parameters.AddWithValue("@id", userId);
        var username = (string?)await checkCmd.ExecuteScalarAsync();
        if (username == null) throw new InvalidOperationException("用户不存在");
        if (username == "admin") throw new InvalidOperationException("不能删除 admin 账户");

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM JOB_USER WHERE ID = @id";
        cmd.Parameters.AddWithValue("@id", userId);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// 初始化默认管理员账户 应用启动时调用
    /// </summary>
    public async Task InitializeDefaultUserAsync()
    {
        await using var conn = new MySqlConnection(AppConfig.ConnectionString);
        await conn.OpenAsync();

        await using var checkCmd = conn.CreateCommand();
        checkCmd.CommandText = "SELECT COUNT(*) FROM JOB_USER WHERE USERNAME = 'admin'";
        var exists = Convert.ToInt32(await checkCmd.ExecuteScalarAsync()) > 0;
        if (exists) return;

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"INSERT INTO JOB_USER (USERNAME, PASSWORD, DISPLAY_NAME, ROLE, IS_ENABLED, CREATED_AT)
                            VALUES ('admin', @password, '系统管理员', 'Admin', 1, @now)";
        cmd.Parameters.AddWithValue("@password", HashPassword("admin123"));
        cmd.Parameters.AddWithValue("@now", DateTime.Now);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// 修改密码
    /// </summary>
    public async Task<bool> ChangePasswordAsync(long userId, string oldPassword, string newPassword)
    {
        await using var conn = new MySqlConnection(AppConfig.ConnectionString);
        await conn.OpenAsync();

        await using var checkCmd = conn.CreateCommand();
        checkCmd.CommandText = "SELECT PASSWORD FROM JOB_USER WHERE ID = @id LIMIT 1";
        checkCmd.Parameters.AddWithValue("@id", userId);
        var storedHash = (string?)await checkCmd.ExecuteScalarAsync();
        if (storedHash == null || storedHash != HashPassword(oldPassword)) return false;

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE JOB_USER SET PASSWORD = @password WHERE ID = @id";
        cmd.Parameters.AddWithValue("@password", HashPassword(newPassword));
        cmd.Parameters.AddWithValue("@id", userId);
        await cmd.ExecuteNonQueryAsync();
        return true;
    }

    /// <summary>
    /// 密码哈希
    /// </summary>
    private static string HashPassword(string password)
    {
        using var sha256 = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(password + "DotJob_Salt_2025");
        var hash = sha256.ComputeHash(bytes);
        return Convert.ToBase64String(hash);
    }

    private static UserEntity MapUser(System.Data.Common.DbDataReader reader) => new()
    {
        Id = reader.GetInt64(0),
        Username = reader.GetString(1),
        Password = reader.GetString(2),
        DisplayName = reader.IsDBNull(3) ? null : reader.GetString(3),
        Email = reader.IsDBNull(4) ? null : reader.GetString(4),
        IsEnabled = reader.GetBoolean(5),
        Role = reader.GetString(6),
        CreatedAt = reader.GetDateTime(7),
        LastLoginAt = reader.IsDBNull(8) ? null : reader.GetDateTime(8)
    };
}