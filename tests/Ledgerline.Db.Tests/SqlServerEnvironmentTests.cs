using Microsoft.Data.SqlClient;

namespace Ledgerline.Db.Tests;

// Checks that the SQL Server instance can run Ledgerline at all, before any
// schema exists. The server name comes from LEDGERLINE_SQL_SERVER and
// defaults to the local default instance.
public class SqlServerEnvironmentTests
{
    // Connections are encrypted by default. A local instance has a
    // self-signed certificate, so the test trusts it.
    private static string ConnectionString =>
        new SqlConnectionStringBuilder
        {
            DataSource = Environment.GetEnvironmentVariable("LEDGERLINE_SQL_SERVER") ?? "localhost",
            InitialCatalog = "master",
            IntegratedSecurity = true,
            TrustServerCertificate = true,
            ConnectTimeout = 5,
        }.ConnectionString;

    // EngineEdition 2 is Standard and 3 is Enterprise, which includes
    // Developer. Express is 4 and has no change data capture.
    [Fact]
    public async Task Edition_supports_change_data_capture()
    {
        var engineEdition = await ScalarAsync<int>(
            "SELECT CAST(SERVERPROPERTY('EngineEdition') AS int);");

        Assert.Contains(engineEdition, new[] { 2, 3 });
    }

    // Change data capture and the nightly jobs both run as SQL Server Agent jobs.
    [Fact]
    public async Task Sql_server_agent_is_running()
    {
        var status = await ScalarAsync<string>(
            "SELECT status_desc FROM sys.dm_server_services WHERE servicename LIKE N'SQL Server Agent%';");

        Assert.Equal("Running", status);
    }

    private static async Task<T> ScalarAsync<T>(string sql)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using var command = new SqlCommand(sql, connection);
        return (T)(await command.ExecuteScalarAsync())!;
    }
}
