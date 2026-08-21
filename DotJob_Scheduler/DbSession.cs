using MySqlConnector;

namespace Job_Scheduler;

/// <summary>
/// 轻量数据库会话：懒加载连接，同一会话内复用，Dispose 时归还连接池。
/// </summary>
internal sealed class DbSession : IAsyncDisposable
{
    private readonly string _connectionString;
    private MySqlConnection? _connection;

    internal DbSession(string connectionString) => _connectionString = connectionString;

    internal async ValueTask<MySqlConnection> GetAsync()
    {
        if (_connection is null)
        {
            _connection = new MySqlConnection(_connectionString);
            await _connection.OpenAsync();
        }
        return _connection;
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
            await _connection.DisposeAsync();
    }
}
