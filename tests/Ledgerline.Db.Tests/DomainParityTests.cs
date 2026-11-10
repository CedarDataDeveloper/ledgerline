using System.Globalization;
using System.Text.RegularExpressions;
using Ledgerline.Domain;

namespace Ledgerline.Db.Tests;

// Every rule that protects money exists twice, in the domain and in the
// database (ADR-006), and the two must be kept in step. These tests put the
// domain's answer next to the database's for the same input, so a change to
// one side that is not made to the other fails here.
//
// JournalLine in this file is the test helper from BankCoreDatabase.cs (one
// row of the procedure's table-valued parameter), not the domain's
// JournalLine: a type in this namespace wins over one brought in by using.
public class DomainParityTests(BankCoreDatabase db) : IClassFixture<BankCoreDatabase>, IAsyncLifetime
{
    public Task InitializeAsync() => db.EnsureOpenBusinessDateAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    // Midpoints in both directions, values with six decimal places like a
    // daily accrual, and ordinary values that are not midpoints. C#'s default
    // banker's rounding disagrees with SQL Server on seven of these.
    private static readonly string[] RoundingVectors =
    [
        "0.005", "0.025", "0.045", "-0.005", "-0.025", "0.015",
        "41.105", "-41.105", "2.675", "-2.675",
        "41.095890", "-41.095890", "1.004999", "123456.785",
    ];

    // Rounding.HalfAwayFromZero and SQL Server's ROUND give the same answer
    // to the fils for every vector, read back in one query.
    [Fact]
    public async Task Rounding_to_the_fils_matches_sql_server_round()
    {
        var values = string.Join(", ", RoundingVectors.Select(vector => $"('{vector}')"));
        var rows = await db.StringsAsync(
            $"""
            SELECT v.x + '|' + CAST(ROUND(CAST(v.x AS DECIMAL(19,6)), 2) AS varchar(40))
            FROM (VALUES {values}) AS v (x);
            """);

        Assert.Equal(RoundingVectors.Length, rows.Count);

        foreach (var row in rows)
        {
            var parts = row.Split('|');
            var value = Parse(parts[0]);
            var sqlRounded = Parse(parts[1]);

            Assert.True(
                Rounding.HalfAwayFromZero(value, 2) == sqlRounded,
                $"{parts[0]}: C# gives {Rounding.HalfAwayFromZero(value, 2)}, SQL Server gives {sqlRounded}");
        }
    }

    [Fact]
    public async Task Every_journal_type_code_is_allowed_by_the_check_constraint_and_no_other()
    {
        var domainCodes = Enum.GetValues<JournalType>().Select(type => type.ToCode());

        Assert.Equal(
            domainCodes.Order(StringComparer.Ordinal),
            await CheckConstraintLiteralsAsync("ck_journal_journal_type"));
    }

    [Fact]
    public async Task Every_account_status_code_is_allowed_by_the_check_constraint_and_no_other()
    {
        var domainCodes = Enum.GetValues<AccountStatus>().Select(status => status.ToCode());

        Assert.Equal(
            domainCodes.Order(StringComparer.Ordinal),
            await CheckConstraintLiteralsAsync("ck_account_status"));
    }

    // Each key the domain refuses, the procedure refuses too (50010).
    [Theory]
    [InlineData("")]
    [InlineData(" abc")]
    [InlineData("abc ")]
    [InlineData("\tabc")]
    [InlineData("abc\n")]
    public Task A_key_the_domain_refuses_is_refused_by_the_posting_procedure(string key) =>
        AssertRefusedOnBothSidesAsync(key);

    [Fact]
    public Task A_key_of_101_characters_is_refused_on_both_sides() =>
        AssertRefusedOnBothSidesAsync(KeyOfLength(101));

    // Each key the domain accepts, the procedure posts. The keys are fixed
    // text; that is safe because this class has a database of its own.
    [Theory]
    [InlineData("q")]
    [InlineData("has inner space")]
    public Task A_key_the_domain_accepts_is_posted_by_the_posting_procedure(string key) =>
        AssertAcceptedOnBothSidesAsync(key);

    [Fact]
    public Task A_key_of_100_characters_is_accepted_on_both_sides() =>
        AssertAcceptedOnBothSidesAsync(KeyOfLength(100));

    // Two keys to the domain are two keys to the database: abc and ABC give
    // two journals.
    [Fact]
    public async Task Keys_that_differ_only_in_case_are_two_keys_on_both_sides()
    {
        var lower = new IdempotencyKey("abc");
        var upper = new IdempotencyKey("ABC");
        Assert.NotEqual(lower, upper);

        var first = await SalaryCreditAsync(lower.Value);
        var second = await SalaryCreditAsync(upper.Value);

        Assert.NotEqual(first, second);
    }

    private async Task AssertRefusedOnBothSidesAsync(string key)
    {
        var error = Assert.Throws<DomainRuleException>(() => new IdempotencyKey(key));
        Assert.Equal(DomainRule.IdempotencyKeyInvalid, error.Rule);

        await BankCoreDatabase.AssertRefusedAsync(() => SalaryCreditAsync(key), 50010);
    }

    // The key goes to the database as the domain holds it (key.Value).
    private async Task AssertAcceptedOnBothSidesAsync(string value)
    {
        var key = new IdempotencyKey(value);

        await SalaryCreditAsync(key.Value);

        Assert.Equal(1, await db.ScalarAsync<int>(
            $"SELECT COUNT(*) FROM ledger.journal WHERE idempotency_key = N'{key.Value}';"));
    }

    // The quoted values in a check constraint's definition, as SQL Server
    // stores it, for example ([status]='CLOSED' OR [status]='FROZEN' OR ...).
    private async Task<List<string>> CheckConstraintLiteralsAsync(string constraintName)
    {
        var definition = await db.ScalarAsync<string>(
            $"SELECT definition FROM sys.check_constraints WHERE name = '{constraintName}';");

        var literals = Regex.Matches(definition, "'([^']*)'")
            .Select(match => match.Groups[1].Value)
            .Order(StringComparer.Ordinal)
            .ToList();

        // Proves the parsing found something: an empty list on both sides
        // would otherwise pass.
        Assert.NotEmpty(literals);
        return literals;
    }

    // A salary credit of 25.00 to a new customer with the given key: Dr cash
    // at central bank (1010), Cr the customer. Returns the journal_id.
    private async Task<long> SalaryCreditAsync(string key)
    {
        var cash = await db.GlLedgerAccountIdAsync("1010");
        var customer = await db.NewCustomerLedgerAccountAsync();
        return await db.PostJournalAsync(key, "SALARY_CREDIT",
            new JournalLine(cash, 25.00m), new JournalLine(customer, -25.00m));
    }

    // A fresh key of exactly the given length: a GUID padded with letters.
    private static string KeyOfLength(int length)
    {
        var start = BankCoreDatabase.NewKey();
        return start + new string('k', length - start.Length);
    }

    private static decimal Parse(string value) => decimal.Parse(value, CultureInfo.InvariantCulture);
}
