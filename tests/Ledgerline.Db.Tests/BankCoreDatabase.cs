using Ledgerline.DbMigrator;
using Ledgerline.Domain;
using Microsoft.Data.SqlClient;

namespace Ledgerline.Db.Tests;

// One throwaway database migrated with the real BankCore scripts, shared by
// all the tests of a class through IClassFixture. Migrating once per class
// keeps the suite quick. Tests that share it must not interfere, so each one
// creates its own customers and accounts and never relies on row counts that
// another test could change.
public sealed class BankCoreDatabase : IAsyncLifetime
{
    private readonly TestDatabase _database = new();

    // Leave unset for the server's default collation. When set, the database
    // is created with this collation before the scripts run.
    public string? Collation { get; init; }

    public async Task InitializeAsync()
    {
        if (Collation is not null)
        {
            await _database.CreateAsync(Collation);
        }

        var result = DatabaseMigrator.Migrate(
            TestDatabase.Server, _database.Name, MigrationScripts.FolderFor("bankcore"));

        if (!result.Successful)
        {
            throw new InvalidOperationException(
                $"Migrating {_database.Name} failed in {result.FailedScript}: {result.Error?.Message}");
        }
    }

    public Task<string> DatabaseCollationAsync() =>
        ScalarAsync<string>("SELECT CAST(DATABASEPROPERTYEX(DB_NAME(), 'Collation') AS nvarchar(128));");

    public Task DisposeAsync() => _database.DropAsync();

    public async Task<SqlConnection> OpenAsync()
    {
        var connection = new SqlConnection(_database.ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    public async Task ExecuteAsync(string sql)
    {
        await using var connection = await OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<T> ScalarAsync<T>(string sql)
    {
        await using var connection = await OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        return (T)(await command.ExecuteScalarAsync())!;
    }

    public async Task<List<string>> StringsAsync(string sql)
    {
        await using var connection = await OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();

        var values = new List<string>();
        while (await reader.ReadAsync())
        {
            values.Add(Convert.ToString(reader.GetValue(0))!);
        }
        return values;
    }

    // A valid customer; tests change one value at a time to break a rule.
    // SCOPE_IDENTITY rather than OUTPUT: SQL Server refuses a plain OUTPUT
    // clause on a table that has a trigger (error 334).
    public Task<long> InsertCustomerAsync(
        string personaCode = "STARTER",
        string homeEmirate = "DXB",
        string kycStatus = "PASSED",
        string status = "ACTIVE") =>
        ScalarAsync<long>(
            $"""
            INSERT INTO core.customer (persona_code, home_emirate, kyc_status, onboarded_on, status)
            VALUES ('{personaCode}', '{homeEmirate}', '{kycStatus}', '2026-11-09', '{status}');
            SELECT CAST(SCOPE_IDENTITY() AS bigint);
            """);

    // The next account number from the sequence, as the application will do
    // it: ask first, then build the IBAN from the number.
    public Task<long> NextAccountIdAsync() =>
        ScalarAsync<long>("SELECT NEXT VALUE FOR core.account_id_seq;");

    // Opens an account with a fresh number and its matching IBAN. Returns the
    // account_id.
    public async Task<long> OpenAccountAsync(long customerId, string productCode, string status = "OPEN")
    {
        var accountId = await NextAccountIdAsync();
        await InsertAccountAsync(accountId, customerId, productCode, SyntheticIban.Generate(accountId), status: status);
        return accountId;
    }

    public Task InsertAccountAsync(
        long accountId,
        long customerId,
        string productCode,
        string iban,
        string currencyCode = "AED",
        string status = "OPEN") =>
        ExecuteAsync(
            $"""
            INSERT INTO core.account (account_id, customer_id, product_code, iban, currency_code, status, opened_on)
            VALUES ({accountId}, {customerId}, '{productCode}', '{iban}', '{currencyCode}', '{status}', '2026-11-09');
            """);

    // A refusal only proves something if the expected rule refused it. Two
    // rules can both reject a row, and SQL Server reports the first one it
    // checks, so assert the error number and the constraint or index name.
    //   547  CHECK or FOREIGN KEY constraint conflict
    //   2601 duplicate key in a unique index
    //   2627 duplicate key in a PRIMARY KEY or UNIQUE constraint
    public static async Task AssertRefusedAsync(Func<Task> action, int errorNumber, string ruleName)
    {
        var error = await Assert.ThrowsAsync<SqlException>(action);

        Assert.Equal(errorNumber, error.Number);
        Assert.Contains(ruleName, error.Message);
    }
}
