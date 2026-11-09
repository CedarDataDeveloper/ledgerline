using Microsoft.Data.SqlClient;

namespace Ledgerline.Db.Tests;

// Account balances come from an indexed view (blueprint 7.4). SQL Server only
// lets a session write to a table under an indexed view when certain SET
// options are on. These tests show that the application's kind of connection
// (SqlClient with its defaults) has them, and what happens when one is off.
public class IndexedViewTests : IAsyncLifetime
{
    private readonly TestDatabase _database = new();

    public async Task InitializeAsync()
    {
        await _database.CreateAsync();

        // A small copy of the balance view: a table of entries and a
        // schema-bound aggregate view with a unique clustered index on it.
        // CREATE VIEW must be alone in its batch, so each runs separately.
        await ExecuteAsync(
            """
            CREATE TABLE dbo.entry (
                entry_id   int IDENTITY(1,1) NOT NULL PRIMARY KEY,
                account_id int NOT NULL,
                amount     decimal(19,4) NOT NULL);
            """);
        await ExecuteAsync(
            """
            CREATE VIEW dbo.v_balance WITH SCHEMABINDING AS
            SELECT account_id,
                   SUM(amount)  AS net_amount,
                   COUNT_BIG(*) AS entry_count
            FROM dbo.entry
            GROUP BY account_id;
            """);
        await ExecuteAsync("CREATE UNIQUE CLUSTERED INDEX ux_v_balance ON dbo.v_balance (account_id);");
    }

    public Task DisposeAsync() => _database.DropAsync();

    [Fact]
    public async Task Indexed_view_works_through_a_default_sqlclient_connection()
    {
        await ExecuteAsync(
            """
            INSERT INTO dbo.entry (account_id, amount)
            VALUES (1, 100.0000), (1, -25.5000), (2, 40.0000);
            """);

        await using var connection = await OpenAsync();
        await using var command = new SqlCommand(
            "SELECT net_amount, entry_count FROM dbo.v_balance WITH (NOEXPAND) WHERE account_id = 1;",
            connection);
        await using var reader = await command.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync());
        Assert.Equal(74.5000m, reader.GetDecimal(0));
        Assert.Equal(2L, reader.GetInt64(1));
    }

    // This is why every migration script starts with the standard header of
    // SET options: a script that ran with QUOTED_IDENTIFIER OFF could not
    // create or maintain an indexed view, and inserts like this one fail.
    [Fact]
    public async Task Insert_with_quoted_identifier_off_is_refused()
    {
        await using var connection = await OpenAsync();
        await using var command = new SqlCommand(
            """
            SET QUOTED_IDENTIFIER OFF;
            INSERT INTO dbo.entry (account_id, amount) VALUES (1, 100.0000);
            """,
            connection);

        var error = await Assert.ThrowsAsync<SqlException>(() => command.ExecuteNonQueryAsync());

        // 1934: the statement failed because of incorrect SET options.
        Assert.Equal(1934, error.Number);
    }

    private async Task<SqlConnection> OpenAsync()
    {
        var connection = new SqlConnection(_database.ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = await OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
