using System.Data;
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

    // When true, read committed snapshot is turned on after migrating.
    public bool ReadCommittedSnapshot { get; init; }

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

        if (ReadCommittedSnapshot)
        {
            // The migrator's pooled connections would hold the database open.
            SqlConnection.ClearAllPools();
            await _database.SetReadCommittedSnapshotOnAsync();
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

    // --- Ledger (LL-004) -------------------------------------------------------

    // The business date every ledger test posts on.
    public const string OpenDate = "2026-11-09";

    // Postings need exactly one OPEN business date (INV-08). Ledger test
    // classes call this before each test; only the first call inserts.
    public Task EnsureOpenBusinessDateAsync() =>
        ExecuteAsync(
            $"""
            IF NOT EXISTS (SELECT 1 FROM ops.business_calendar WHERE status = 'OPEN')
                INSERT INTO ops.business_calendar (business_date, status) VALUES ('{OpenDate}', 'OPEN');
            """);

    // A fresh idempotency key, so tests never collide with each other.
    public static string NewKey() => "TEST-" + Guid.NewGuid();

    public Task<long> GlLedgerAccountIdAsync(string glCode) =>
        ScalarAsync<long>($"SELECT ledger_account_id FROM ledger.ledger_account WHERE gl_code = '{glCode}';");

    public Task<long> LedgerAccountIdAsync(long accountId) =>
        ScalarAsync<long>($"SELECT ledger_account_id FROM ledger.ledger_account WHERE account_id = {accountId};");

    // A new customer with one savings account (savings, so the one-salary-
    // account rule never gets in the way). Returns its ledger_account_id.
    public async Task<long> NewCustomerLedgerAccountAsync(string status = "OPEN")
    {
        var customerId = await InsertCustomerAsync();
        var accountId = await OpenAccountAsync(customerId, "SAVINGS_AED", status);
        return await LedgerAccountIdAsync(accountId);
    }

    // Freezes, closes or reopens the customer account behind a ledger account.
    public Task SetAccountStatusAsync(long ledgerAccountId, string status) =>
        ExecuteAsync(
            $"""
            UPDATE a SET status = '{status}'
            FROM core.account AS a
            JOIN ledger.ledger_account AS la ON la.account_id = a.account_id
            WHERE la.ledger_account_id = {ledgerAccountId};
            """);

    // Calls ledger.usp_post_journal on a new connection. Returns the journal_id.
    public async Task<long> PostJournalAsync(string? idempotencyKey, string journalType, params JournalLine[] lines)
    {
        await using var connection = await OpenAsync();
        return await PostJournalAsync(connection, null, idempotencyKey, journalType, lines);
    }

    // Calls ledger.usp_post_journal the way the application will: the lines
    // travel as one table-valued parameter of type ledger.journal_line.
    public static async Task<long> PostJournalAsync(
        SqlConnection connection, SqlTransaction? transaction, string? idempotencyKey, string journalType, JournalLine[] lines)
    {
        var table = new DataTable();
        table.Columns.Add("ledger_account_id", typeof(long));
        table.Columns.Add("amount", typeof(decimal));
        table.Columns.Add("currency_code", typeof(string));
        foreach (var line in lines)
        {
            table.Rows.Add(line.LedgerAccountId, line.Amount, line.CurrencyCode);
        }

        await using var command = new SqlCommand("ledger.usp_post_journal", connection, transaction)
        {
            CommandType = CommandType.StoredProcedure,
        };
        // Size 400, as the procedure declares it. SqlClient silently cuts a
        // value to the parameter's size, so a size of 100 would hide an
        // over-long key from the procedure's length check.
        command.Parameters.Add("@idempotency_key", SqlDbType.NVarChar, 400).Value = (object?)idempotencyKey ?? DBNull.Value;
        command.Parameters.Add("@journal_type", SqlDbType.VarChar, 30).Value = journalType;

        var linesParameter = command.Parameters.Add("@lines", SqlDbType.Structured);
        linesParameter.TypeName = "ledger.journal_line";
        linesParameter.Value = table;

        var journalId = command.Parameters.Add("@journal_id", SqlDbType.BigInt);
        journalId.Direction = ParameterDirection.Output;

        await command.ExecuteNonQueryAsync();
        return (long)journalId.Value;
    }

    // Puts money into a customer account: Dr cash at central bank (1010),
    // Cr the customer, as a salary credit does.
    public async Task FundAsync(long customerLedgerAccountId, decimal amount)
    {
        var cash = await GlLedgerAccountIdAsync("1010");
        await PostJournalAsync(NewKey(), "SALARY_CREDIT",
            new JournalLine(cash, amount), new JournalLine(customerLedgerAccountId, -amount));
    }

    // The sum of an account's entries, read from the balance view. An account
    // with no entries has no row in the view, so it reads as zero.
    public Task<decimal> NetAmountAsync(long ledgerAccountId) =>
        ScalarAsync<decimal>(
            $"""
            SELECT ISNULL((SELECT net_amount
                           FROM ledger.v_account_balance WITH (NOEXPAND)
                           WHERE ledger_account_id = {ledgerAccountId}), 0);
            """);

    // Customer accounts are liabilities of the bank, so the balance the
    // customer sees is the negative of the sum of the entries.
    public async Task<decimal> CustomerBalanceAsync(long ledgerAccountId) =>
        -await NetAmountAsync(ledgerAccountId);

    // Runs every posting at the same moment. All connections are opened
    // first, then one start signal releases every call together, so the
    // calls really overlap inside the database. Each outcome is either the
    // journal_id or the SQL Server error number.
    public async Task<List<PostingOutcome>> PostAllAtOnceAsync(
        IReadOnlyList<(string Key, string JournalType, JournalLine[] Lines)> postings)
    {
        var connections = new List<SqlConnection>();
        try
        {
            foreach (var _ in postings)
            {
                connections.Add(await OpenAsync());
            }

            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var calls = postings.Select((posting, i) => Task.Run(async () =>
            {
                await start.Task;
                try
                {
                    var journalId = await PostJournalAsync(connections[i], null, posting.Key, posting.JournalType, posting.Lines);
                    return new PostingOutcome(journalId, null);
                }
                catch (SqlException error)
                {
                    return new PostingOutcome(null, error.Number);
                }
            })).ToList();

            start.SetResult();
            return [.. await Task.WhenAll(calls)];
        }
        finally
        {
            foreach (var connection in connections)
            {
                await connection.DisposeAsync();
            }
        }
    }

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

    // The ledger's own refusals (THROW 50001 to 50011, listed at the top of
    // 0006_post_journal.sql) each have a number of their own, so the number
    // alone names the rule.
    public static async Task AssertRefusedAsync(Func<Task> action, int errorNumber)
    {
        var error = await Assert.ThrowsAsync<SqlException>(action);

        Assert.Equal(errorNumber, error.Number);
    }
}

// One line of a journal. Positive amount = debit, negative = credit.
public sealed record JournalLine(long LedgerAccountId, decimal Amount, string CurrencyCode = "AED");

// What one posting attempt ended with: a journal, or an error number.
public sealed record PostingOutcome(long? JournalId, int? ErrorNumber);
