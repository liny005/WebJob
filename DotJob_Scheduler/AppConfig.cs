namespace Job_Scheduler;

/// <summary>
/// 应用配置类 - 用于存储数据库连接配置
/// </summary>
public static class AppConfig
{
    /// <summary>
    /// 数据库提供程序名称
    /// 支持: SQLite, SQLite-Microsoft, MySql, SqlServer, SQLServerMOT, Npgsql, OracleODPManaged, Firebird
    /// </summary>
    public static string DbProviderName { get; set; } = "MySql";

    /// <summary>
    /// 数据库连接字符串
    /// </summary>
    public static string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// 调度器名称
    /// </summary>
    public static string SchedulerName { get; set; } = "jobScheduler";

    /// <summary>
    /// 线程池最大并发数
    /// </summary>
    public static int MaxConcurrency { get; set; } = 100;

    /// <summary>
    /// 每批最多获取的待触发任务数
    /// </summary>
    public static int MaxBatchSize { get; set; } = 100;

    /// <summary>
    /// 从配置文件初始化
    /// </summary>
    public static void Initialize(IConfiguration configuration)
    {
        ConnectionString = configuration.GetConnectionString("MysqlConnection") ?? string.Empty;

        // 可以从配置文件读取 DbProviderName，默认使用 MySql
        DbProviderName = configuration.GetValue<string>("Quartz:DbProviderName") ?? "MySql";

        SchedulerName = configuration.GetValue<string>("Quartz:SchedulerName") ?? "jobScheduler";

        MaxConcurrency = configuration.GetValue<int?>("Quartz:MaxConcurrency") ?? 300;

        MaxBatchSize = configuration.GetValue<int?>("Quartz:MaxBatchSize") ?? 300;
    }
}
