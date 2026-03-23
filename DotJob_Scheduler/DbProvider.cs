using System.Data.Common;
using MySql.Data.MySqlClient;
using Quartz.Impl.AdoJobStore.Common;

namespace Job_Scheduler;

/// <summary>
/// 数据库连接提供程序
/// </summary>
public class DbProvider : IDbProvider
{
    private readonly string _connectionString;

    public DbProvider(string dbProviderName, string connectionString)
    {
        _connectionString = connectionString;
        Metadata = new DbMetadata
        {
            AssemblyName = GetAssemblyName(dbProviderName),
            ConnectionType = GetConnectionType(dbProviderName),
            CommandType = GetCommandType(dbProviderName),
            ParameterType = GetParameterType(dbProviderName),
            ParameterDbType = GetParameterDbType(dbProviderName),
            ParameterDbTypePropertyName = GetParameterDbTypePropertyName(dbProviderName),
            ParameterNamePrefix = GetParameterNamePrefix(dbProviderName),
            ExceptionType = GetExceptionType(dbProviderName),
            BindByName = true
        };
    }

    public string ConnectionString
    {
        get => _connectionString;
        set { }
    }

    public DbMetadata Metadata { get; }

    public DbConnection CreateConnection()
    {
        var connection = new MySqlConnection(_connectionString);
        return connection;
    }

    public DbCommand CreateCommand()
    {
        return new MySqlCommand();
    }

    public void Initialize()
    {
        // 初始化操作
    }

    public void Shutdown()
    {
        // 关闭操作
    }

    #region 获取数据库元数据

    private static string GetAssemblyName(string dbProviderName)
    {
        return dbProviderName switch
        {
            "MySql" => "MySql.Data",
            "SqlServer" or "SQLServerMOT" => "Microsoft.Data.SqlClient",
            "Npgsql" => "Npgsql",
            "SQLite" or "SQLite-Microsoft" => "Microsoft.Data.Sqlite",
            "OracleODPManaged" => "Oracle.ManagedDataAccess",
            "Firebird" => "FirebirdSql.Data.FirebirdClient",
            _ => throw new ArgumentException($"Unsupported database provider: {dbProviderName}")
        };
    }

    private static Type GetConnectionType(string dbProviderName)
    {
        // 目前只支持 MySQL，其他数据库类型需要添加对应的包引用
        return typeof(MySqlConnection);
    }

    private static Type GetCommandType(string dbProviderName)
    {
        return typeof(MySqlCommand);
    }

    private static Type GetParameterType(string dbProviderName)
    {
        return typeof(MySqlParameter);
    }

    private static Type GetParameterDbType(string dbProviderName)
    {
        return typeof(MySqlDbType);
    }

    private static string GetParameterDbTypePropertyName(string dbProviderName)
    {
        return dbProviderName switch
        {
            "MySql" => "MySqlDbType",
            "SqlServer" or "SQLServerMOT" => "SqlDbType",
            "Npgsql" => "NpgsqlDbType",
            "SQLite" or "SQLite-Microsoft" => "DbType",
            "OracleODPManaged" => "OracleDbType",
            "Firebird" => "FbDbType",
            _ => "MySqlDbType"
        };
    }

    private static string GetParameterNamePrefix(string dbProviderName)
    {
        return dbProviderName switch
        {
            "MySql" => "?",
            "SqlServer" or "SQLServerMOT" => "@",
            "Npgsql" => ":",
            "SQLite" or "SQLite-Microsoft" => "@",
            "OracleODPManaged" => ":",
            "Firebird" => "@",
            _ => "?"
        };
    }

    private static Type GetExceptionType(string dbProviderName)
    {
        return typeof(MySqlException);
    }

    #endregion
}
