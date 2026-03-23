using MySql.Data.MySqlClient;

namespace Job_Scheduler;

/// <summary>
/// 数据库自动初始化
/// 应用启动时检查数据库和表是否存在，不存在则自动创建，保证幂等性。
/// </summary>
public static class DatabaseInitializer
{
    // 用这张表作为"是否已初始化"的判断依据
    private const string SentinelTable = "JOB_USER";

    public static async Task InitializeAsync(string connectionString, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            logger.LogWarning("[DB Init] 连接字符串为空，跳过数据库初始化");
            return;
        }

        var sqlFile = Path.Combine(AppContext.BaseDirectory, "init_database.sql");
        if (!File.Exists(sqlFile))
            throw new FileNotFoundException($"[DB Init] 初始化 SQL 文件不存在: {sqlFile}");

        try
        {
            // ── 第一步：确保 Database 本身存在 ──────────────────────────────
            var csb = new MySqlConnectionStringBuilder(connectionString);
            var dbName = csb.Database;

            csb.Database = string.Empty;
            await using var setupConn = new MySqlConnection(csb.ConnectionString);
            await setupConn.OpenAsync();

            await using var createDbCmd = setupConn.CreateCommand();
            createDbCmd.CommandText =
                $"CREATE DATABASE IF NOT EXISTS `{dbName}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;";
            await createDbCmd.ExecuteNonQueryAsync();

            // ── 第二步：检查 Sentinel 表是否已存在，已存在则跳过 ────────────
            await using var checkCmd = setupConn.CreateCommand();
            checkCmd.CommandText = """
                SELECT COUNT(*) FROM information_schema.TABLES
                WHERE TABLE_SCHEMA = @db AND TABLE_NAME = @table;
                """;
            checkCmd.Parameters.AddWithValue("@db", dbName);
            checkCmd.Parameters.AddWithValue("@table", SentinelTable);

            var exists = Convert.ToInt32(await checkCmd.ExecuteScalarAsync()) > 0;
            if (exists)
            {
                logger.LogInformation("[DB Init] 数据库表已存在，跳过初始化");
                return;
            }

            // ── 第三步：首次运行，执行完整建表 + 默认数据 ───────────────────
            logger.LogInformation("[DB Init] 首次启动，正在初始化数据库表结构...");

            var sql = await File.ReadAllTextAsync(sqlFile);

            await using var appConn = new MySqlConnection(connectionString);
            await appConn.OpenAsync();

            var script = new MySqlScript(appConn, sql);
            script.Error += (_, args) =>
            {
                if (args.Exception is MySqlException { Number: 1062 }) // ER_DUP_ENTRY
                    args.Ignore = true;
                else
                    logger.LogWarning("[DB Init] SQL 执行警告: {Message}", args.Exception.Message);
            };

            var count = await script.ExecuteAsync();
            logger.LogInformation("[DB Init] 初始化完成，共执行 {Count} 条语句", count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[DB Init] 数据库初始化失败，应用将终止");
            throw;
        }
    }
}
