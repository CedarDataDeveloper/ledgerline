using Ledgerline.DbMigrator;
using Microsoft.Data.SqlClient;

namespace Ledgerline.Db.Tests;

// Runs the real migration scripts, and some deliberately broken ones, against
// a throwaway database. xUnit makes a new instance of this class for every
// test, so each test gets its own database, dropped even when the test fails.
public class MigrationTests : IAsyncLifetime
{
    private readonly TestDatabase _database = new();

    // The migrator creates the database itself, so there is nothing to set up.
    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => _database.DropAsync();

    // The real db\<database>\migrations folder in the repository.
    private static string RealScriptsFolder(string database) =>
        MigrationScripts.FolderFor(database);

    [Fact]
    public async Task BankCore_scripts_create_the_bankcore_schemas()
    {
        var result = DatabaseMigrator.Migrate(TestDatabase.Server, _database.Name, RealScriptsFolder("bankcore"));

        Assert.True(result.Successful, result.Error?.Message);
        var schemas = await SchemasAsync();
        Assert.Contains("core", schemas);
        Assert.Contains("ledger", schemas);
        Assert.Contains("ops", schemas);
        Assert.Contains("pii", schemas);
    }

    [Fact]
    public async Task Warehouse_scripts_create_the_warehouse_schemas()
    {
        var result = DatabaseMigrator.Migrate(TestDatabase.Server, _database.Name, RealScriptsFolder("warehouse"));

        Assert.True(result.Successful, result.Error?.Message);
        var schemas = await SchemasAsync();
        Assert.Contains("bronze", schemas);
        Assert.Contains("etl", schemas);
        Assert.Contains("recon", schemas);

        // dbt creates silver and gold in later tickets, not the migrations.
        Assert.DoesNotContain("silver", schemas);
        Assert.DoesNotContain("gold", schemas);
    }

    [Fact]
    public void A_second_run_applies_nothing()
    {
        var first = DatabaseMigrator.Migrate(TestDatabase.Server, _database.Name, RealScriptsFolder("bankcore"));
        var second = DatabaseMigrator.Migrate(TestDatabase.Server, _database.Name, RealScriptsFolder("bankcore"));

        Assert.True(first.Successful, first.Error?.Message);
        Assert.Contains("0001_schemas.sql", first.ScriptsApplied);
        Assert.True(second.Successful, second.Error?.Message);
        Assert.Empty(second.ScriptsApplied);
    }

    // Each script runs in its own transaction. A script that fails part way
    // must leave nothing behind and must not be recorded as applied, so that
    // fixing it and running again is safe.
    [Fact]
    public async Task A_failing_script_leaves_nothing_behind_and_is_not_recorded()
    {
        var folder = Path.Combine(Path.GetTempPath(), _database.Name);
        Directory.CreateDirectory(folder);
        try
        {
            File.WriteAllText(Path.Combine(folder, "0001_good.sql"),
                "CREATE TABLE dbo.good_table (id int NOT NULL);");

            // The first batch succeeds, the second fails.
            File.WriteAllText(Path.Combine(folder, "0002_bad.sql"),
                "CREATE TABLE dbo.half_done_table (id int NOT NULL);\nGO\nTHROW 50000, N'Deliberate failure', 1;\n");

            var result = DatabaseMigrator.Migrate(TestDatabase.Server, _database.Name, folder);

            Assert.False(result.Successful);
            Assert.NotNull(result.Error);
            Assert.Equal("0002_bad.sql", result.FailedScript);
            Assert.Equal(new[] { "0001_good.sql" }, result.ScriptsApplied);

            var tables = await TablesAsync();
            Assert.Contains("good_table", tables);
            Assert.DoesNotContain("half_done_table", tables);

            var journal = await JournalAsync();
            Assert.Contains("0001_good.sql", journal);
            Assert.DoesNotContain("0002_bad.sql", journal);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private async Task<List<string>> SchemasAsync() =>
        await StringsAsync("SELECT name FROM sys.schemas;");

    // DbUp's default journal table.
    private async Task<List<string>> JournalAsync() =>
        await StringsAsync("SELECT ScriptName FROM dbo.SchemaVersions;");

    private async Task<List<string>> TablesAsync() =>
        await StringsAsync("SELECT name FROM sys.tables;");

    private async Task<List<string>> StringsAsync(string sql)
    {
        await using var connection = new SqlConnection(_database.ConnectionString);
        await connection.OpenAsync();

        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();

        var values = new List<string>();
        while (await reader.ReadAsync())
        {
            values.Add(Convert.ToString(reader.GetValue(0))!);
        }
        return values;
    }
}
