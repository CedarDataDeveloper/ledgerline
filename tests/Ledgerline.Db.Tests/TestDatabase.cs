using System.Security.Cryptography;
using Microsoft.Data.SqlClient;

namespace Ledgerline.Db.Tests;

// A throwaway database named LedgerlineTest_<random hex>. Tests never touch
// the real BankCore or Warehouse; each one works in its own database and
// drops it afterwards.
public sealed class TestDatabase
{
    public static string Server =>
        Environment.GetEnvironmentVariable("LEDGERLINE_SQL_SERVER") ?? "localhost";

    public string Name { get; } = "LedgerlineTest_" + RandomNumberGenerator.GetHexString(12, lowercase: true);

    public string ConnectionString => ConnectionStringFor(Name);

    // Connections are encrypted by default. A local instance has a
    // self-signed certificate, so the tests trust it.
    public static string ConnectionStringFor(string database) =>
        new SqlConnectionStringBuilder
        {
            DataSource = Server,
            InitialCatalog = database,
            IntegratedSecurity = true,
            TrustServerCertificate = true,
            ConnectTimeout = 5,
        }.ConnectionString;

    public async Task CreateAsync()
    {
        await ExecuteInMasterAsync($"CREATE DATABASE [{Name}];");
    }

    // Safe to call whether or not the database was ever created.
    public async Task DropAsync()
    {
        // Pooled connections stay open after a test and would block the drop.
        SqlConnection.ClearAllPools();

        await ExecuteInMasterAsync(
            $"""
            IF DB_ID(N'{Name}') IS NOT NULL
            BEGIN
                ALTER DATABASE [{Name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                DROP DATABASE [{Name}];
            END;
            """);
    }

    private static async Task ExecuteInMasterAsync(string sql)
    {
        await using var connection = new SqlConnection(ConnectionStringFor("master"));
        await connection.OpenAsync();

        await using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
