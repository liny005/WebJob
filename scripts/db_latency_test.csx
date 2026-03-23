#!/usr/bin/env dotnet-script
// 数据库延迟测试脚本
// 使用方式: dotnet script db_latency_test.csx
// 或直接运行 C# 代码

#r "nuget: MySql.Data, 9.5.0"

using MySql.Data.MySqlClient;
using System.Diagnostics;

const string connectionString =
    "Server=localhost;Port=3306;Uid=testuser;Pwd=root123456;Database=job;Charset=utf8mb4;Connection Timeout=15;Default Command Timeout=15";
const int warmupRounds = 3;
const int testRounds = 10;

Console.WriteLine("========================================");
Console.WriteLine("      DotJob 数据库延迟测试工具         ");
Console.WriteLine("========================================");
Console.WriteLine($"服务器: localhost:3306");
Console.WriteLine($"数据库: job");
Console.WriteLine($"预热次数: {warmupRounds} 次");
Console.WriteLine($"测试次数: {testRounds} 次");
Console.WriteLine("----------------------------------------");

// 1. 测试连接建立耗时
Console.WriteLine("\n[1] 测试 TCP 连接 + 握手延迟...");
var connectTimes = new List<double>();
for (int i = 0; i < testRounds; i++)
{
    var sw = Stopwatch.StartNew();
    try
    {
        using var conn = new MySqlConnection(connectionString);
        conn.Open();
        sw.Stop();
        connectTimes.Add(sw.Elapsed.TotalMilliseconds);
        Console.WriteLine($"  第 {i + 1,2} 次连接: {sw.Elapsed.TotalMilliseconds:F2} ms");
    }
    catch (Exception ex)
    {
        sw.Stop();
        Console.WriteLine($"  第 {i + 1,2} 次连接: ❌ 失败 - {ex.Message}");
    }
}

if (connectTimes.Count > 0)
{
    Console.WriteLine($"\n  连接延迟统计:");
    Console.WriteLine($"    最小值: {connectTimes.Min():F2} ms");
    Console.WriteLine($"    最大值: {connectTimes.Max():F2} ms");
    Console.WriteLine($"    平均值: {connectTimes.Average():F2} ms");
    Console.WriteLine($"    成功率: {connectTimes.Count}/{testRounds}");
}

// 2. 使用连接池测试查询延迟（SELECT 1）
Console.WriteLine("\n[2] 测试 SELECT 1 查询延迟（使用连接池）...");
var queryTimes = new List<double>();

// 预热
using (var warmConn = new MySqlConnection(connectionString))
{
    warmConn.Open();
    for (int i = 0; i < warmupRounds; i++)
    {
        using var cmd = new MySqlCommand("SELECT 1", warmConn);
        cmd.ExecuteScalar();
    }
}

// 正式测试
for (int i = 0; i < testRounds; i++)
{
    try
    {
        using var conn = new MySqlConnection(connectionString);
        conn.Open();
        var sw = Stopwatch.StartNew();
        using var cmd = new MySqlCommand("SELECT 1", conn);
        cmd.ExecuteScalar();
        sw.Stop();
        queryTimes.Add(sw.Elapsed.TotalMilliseconds);
        Console.WriteLine($"  第 {i + 1,2} 次查询: {sw.Elapsed.TotalMilliseconds:F2} ms");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  第 {i + 1,2} 次查询: ❌ 失败 - {ex.Message}");
    }
}

if (queryTimes.Count > 0)
{
    var sorted = queryTimes.OrderBy(x => x).ToList();
    var p95 = sorted[(int)(sorted.Count * 0.95)];
    var p99 = sorted.Last();
    Console.WriteLine($"\n  查询延迟统计:");
    Console.WriteLine($"    最小值: {queryTimes.Min():F2} ms");
    Console.WriteLine($"    最大值: {queryTimes.Max():F2} ms");
    Console.WriteLine($"    平均值: {queryTimes.Average():F2} ms");
    Console.WriteLine($"    P95:    {p95:F2} ms");
    Console.WriteLine($"    P99:    {p99:F2} ms");
    Console.WriteLine($"    成功率: {queryTimes.Count}/{testRounds}");
}

// 3. 测试实际业务表查询
Console.WriteLine("\n[3] 测试业务表查询延迟...");
var tables = new[] { "qrtz_job_details", "qrtz_triggers", "sys_user" };

using var bizConn = new MySqlConnection(connectionString);
bizConn.Open();

foreach (var table in tables)
{
    try
    {
        var sw = Stopwatch.StartNew();
        using var cmd = new MySqlCommand($"SELECT COUNT(*) FROM {table}", bizConn);
        var count = cmd.ExecuteScalar();
        sw.Stop();
        Console.WriteLine($"  SELECT COUNT(*) FROM {table,-20}: {sw.Elapsed.TotalMilliseconds:F2} ms  (共 {count} 行)");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  {table,-20}: ⚠️  跳过 - {ex.Message}");
    }
}

// 4. 测试写入延迟（INSERT + DELETE）
Console.WriteLine("\n[4] 测试写入延迟（INSERT + DELETE）...");
var writeTimes = new List<double>();
try
{
    // 尝试找一个可以测试写入的表
    using var writeConn = new MySqlConnection(connectionString);
    writeConn.Open();

    // 先检查是否存在测试表
    bool tableExists = false;
    using (var checkCmd = new MySqlCommand(
        "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'job' AND table_name = 'latency_test'",
        writeConn))
    {
        tableExists = Convert.ToInt64(checkCmd.ExecuteScalar()!) > 0;
    }

    if (!tableExists)
    {
        using var createCmd = new MySqlCommand(
            "CREATE TABLE latency_test (id INT AUTO_INCREMENT PRIMARY KEY, val VARCHAR(32), created_at DATETIME DEFAULT NOW())",
            writeConn);
        createCmd.ExecuteNonQuery();
        Console.WriteLine("  已创建临时测试表 latency_test");
    }

    for (int i = 0; i < testRounds; i++)
    {
        var sw = Stopwatch.StartNew();
        using var insertCmd = new MySqlCommand(
            "INSERT INTO latency_test (val) VALUES (@v)", writeConn);
        insertCmd.Parameters.AddWithValue("@v", Guid.NewGuid().ToString("N")[..8]);
        insertCmd.ExecuteNonQuery();
        sw.Stop();
        writeTimes.Add(sw.Elapsed.TotalMilliseconds);
        Console.WriteLine($"  第 {i + 1,2} 次 INSERT: {sw.Elapsed.TotalMilliseconds:F2} ms");
    }

    // 清理测试数据
    using var cleanCmd = new MySqlCommand("DROP TABLE latency_test", writeConn);
    cleanCmd.ExecuteNonQuery();
    Console.WriteLine("  已清理临时测试表");

    if (writeTimes.Count > 0)
    {
        Console.WriteLine($"\n  写入延迟统计:");
        Console.WriteLine($"    最小值: {writeTimes.Min():F2} ms");
        Console.WriteLine($"    最大值: {writeTimes.Max():F2} ms");
        Console.WriteLine($"    平均值: {writeTimes.Average():F2} ms");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"  ❌ 写入测试失败: {ex.Message}");
}

Console.WriteLine("\n========================================");
Console.WriteLine("             测试完成");
Console.WriteLine("========================================");

