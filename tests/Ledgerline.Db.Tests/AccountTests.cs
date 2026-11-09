using Ledgerline.Domain;

namespace Ledgerline.Db.Tests;

// core.account: the IBAN check, the one-salary-account rule and the account
// number sequence (blueprint 7.1, BR-02).
public class AccountTests(BankCoreDatabase db) : IClassFixture<BankCoreDatabase>
{
    // --- IBAN -----------------------------------------------------------------

    [Fact]
    public async Task A_generated_iban_is_accepted()
    {
        var customerId = await db.InsertCustomerAsync();
        var accountId = await db.NextAccountIdAsync();
        var iban = SyntheticIban.Generate(accountId);

        await db.InsertAccountAsync(accountId, customerId, "SAVINGS_AED", iban);

        Assert.Equal(iban, await db.ScalarAsync<string>($"SELECT iban FROM core.account WHERE account_id = {accountId};"));
    }

    // The table and the Domain code must agree on what a valid IBAN is, from
    // the smallest account number to the largest.
    [Theory]
    [InlineData(1L)]
    [InlineData(97L)]
    [InlineData(1234567890123456L)]
    [InlineData(9999999999999999L)]
    public async Task The_table_accepts_what_SyntheticIban_generates(long accountNumber)
    {
        var customerId = await db.InsertCustomerAsync();
        var accountId = await db.NextAccountIdAsync();

        // The IBAN is built from a chosen number rather than the account_id,
        // to reach the edges of the range. Nothing in the table ties the two.
        await db.InsertAccountAsync(accountId, customerId, "SAVINGS_AED", SyntheticIban.Generate(accountNumber));
    }

    [Fact]
    public async Task An_iban_with_one_changed_digit_is_refused_by_the_iban_check()
    {
        var customerId = await db.InsertCustomerAsync();
        var accountId = await db.NextAccountIdAsync();
        var iban = SyntheticIban.Generate(accountId);

        // Change the last digit, so the layout and bank code are still right
        // and only the mod-97 part of the check can refuse it.
        var last = iban[^1] == '9' ? '0' : (char)(iban[^1] + 1);
        var changed = iban[..^1] + last;

        await BankCoreDatabase.AssertRefusedAsync(
            () => db.InsertAccountAsync(accountId, customerId, "SAVINGS_AED", changed), 547, "ck_account_iban");
    }

    [Fact]
    public async Task An_iban_with_a_letter_among_the_digits_is_refused_by_the_iban_check()
    {
        var customerId = await db.InsertCustomerAsync();
        var accountId = await db.NextAccountIdAsync();
        var iban = SyntheticIban.Generate(accountId);
        var withLetter = iban[..20] + "X" + iban[21..];

        await BankCoreDatabase.AssertRefusedAsync(
            () => db.InsertAccountAsync(accountId, customerId, "SAVINGS_AED", withLetter), 547, "ck_account_iban");
    }

    // The server collation ignores case, so the pattern must be made exact:
    // SyntheticIban.IsValid refuses a lower-case prefix, and so must the table.
    [Fact]
    public async Task An_iban_with_a_lower_case_prefix_is_refused_by_the_iban_check()
    {
        var customerId = await db.InsertCustomerAsync();
        var accountId = await db.NextAccountIdAsync();
        var lowerCase = "ae" + SyntheticIban.Generate(accountId)[2..];

        await BankCoreDatabase.AssertRefusedAsync(
            () => db.InsertAccountAsync(accountId, customerId, "SAVINGS_AED", lowerCase), 547, "ck_account_iban");
    }

    // Under some collations LIKE '[0-9]' also matches a superscript two (²).
    // TRY_CAST would then give NULL, and a check constraint lets NULL through.
    // This server's collation does not do that; the test guards against a
    // server that does.
    [Fact]
    public async Task An_iban_with_a_superscript_digit_is_refused_by_the_iban_check()
    {
        var customerId = await db.InsertCustomerAsync();
        var accountId = await db.NextAccountIdAsync();
        var iban = SyntheticIban.Generate(accountId);
        var withSuperscript = iban[..20] + "²" + iban[21..];

        await BankCoreDatabase.AssertRefusedAsync(
            () => db.InsertAccountAsync(accountId, customerId, "SAVINGS_AED", withSuperscript), 547, "ck_account_iban");
    }

    // Correct check digits, but bank code 033 belongs to a real bank. Only
    // Ledgerline's fictional code 999 is allowed.
    [Fact]
    public async Task A_real_bank_iban_is_refused_by_the_iban_check()
    {
        var customerId = await db.InsertCustomerAsync();
        var accountId = await db.NextAccountIdAsync();

        await BankCoreDatabase.AssertRefusedAsync(
            () => db.InsertAccountAsync(accountId, customerId, "SAVINGS_AED", "AE070331234567890123456"), 547, "ck_account_iban");
    }

    [Fact]
    public async Task A_duplicate_iban_is_refused_by_the_unique_constraint()
    {
        var customerId = await db.InsertCustomerAsync();
        var firstId = await db.OpenAccountAsync(customerId, "SAVINGS_AED");
        var secondId = await db.NextAccountIdAsync();

        // Savings accounts, so the one-salary-account index cannot fire first.
        await BankCoreDatabase.AssertRefusedAsync(
            () => db.InsertAccountAsync(secondId, customerId, "SAVINGS_AED", SyntheticIban.Generate(firstId)),
            2627, "uq_account_iban");
    }

    // --- One open salary account per customer (BR-02) -------------------------

    [Fact]
    public async Task A_second_open_salary_account_is_refused()
    {
        var customerId = await db.InsertCustomerAsync();
        await db.OpenAccountAsync(customerId, "CURRENT_AED");

        await BankCoreDatabase.AssertRefusedAsync(
            () => db.OpenAccountAsync(customerId, "CURRENT_AED"), 2601, "ux_account_one_open_salary");
    }

    // A frozen salary account still counts: it is not closed.
    [Fact]
    public async Task A_second_salary_account_beside_a_frozen_one_is_refused()
    {
        var customerId = await db.InsertCustomerAsync();
        await db.OpenAccountAsync(customerId, "CURRENT_AED", status: "FROZEN");

        await BankCoreDatabase.AssertRefusedAsync(
            () => db.OpenAccountAsync(customerId, "CURRENT_AED"), 2601, "ux_account_one_open_salary");
    }

    // The limit of three savings accounts is application logic (LL-007); the
    // table only stops a second salary account.
    [Fact]
    public async Task Two_savings_accounts_are_accepted()
    {
        var customerId = await db.InsertCustomerAsync();
        await db.OpenAccountAsync(customerId, "SAVINGS_AED");
        await db.OpenAccountAsync(customerId, "SAVINGS_AED");

        Assert.Equal(2, await db.ScalarAsync<int>($"SELECT COUNT(*) FROM core.account WHERE customer_id = {customerId};"));
    }

    [Fact]
    public async Task A_new_salary_account_is_accepted_after_the_old_one_is_closed()
    {
        var customerId = await db.InsertCustomerAsync();
        var oldId = await db.OpenAccountAsync(customerId, "CURRENT_AED");
        await db.ExecuteAsync($"UPDATE core.account SET status = 'CLOSED' WHERE account_id = {oldId};");

        await db.OpenAccountAsync(customerId, "CURRENT_AED");

        Assert.Equal(2, await db.ScalarAsync<int>(
            $"SELECT COUNT(*) FROM core.account WHERE customer_id = {customerId} AND product_code = 'CURRENT_AED';"));
    }

    // --- Account numbers ------------------------------------------------------

    // When the caller leaves account_id out, the default takes the next
    // number from core.account_id_seq. Tests in this class run one at a time,
    // so nothing else draws from the sequence between the insert and the check.
    [Fact]
    public async Task Account_id_comes_from_the_sequence_when_not_supplied()
    {
        var customerId = await db.InsertCustomerAsync();

        // Any valid IBAN will do here; the number it carries is not checked
        // against account_id. OUTPUT goes INTO a table variable because the
        // table has a trigger (error 334 otherwise).
        var accountId = await db.ScalarAsync<long>(
            $"""
            DECLARE @inserted TABLE (account_id bigint);
            INSERT INTO core.account (customer_id, product_code, iban, currency_code, status, opened_on)
            OUTPUT inserted.account_id INTO @inserted
            VALUES ({customerId}, 'SAVINGS_AED', '{SyntheticIban.Generate(42)}', 'AED', 'OPEN', '2026-11-09');
            SELECT account_id FROM @inserted;
            """);

        var sequenceNow = await db.ScalarAsync<long>(
            "SELECT CAST(current_value AS bigint) FROM sys.sequences WHERE object_id = OBJECT_ID(N'core.account_id_seq');");

        Assert.True(accountId >= 1000000001, $"account_id {accountId} is below the sequence start");
        Assert.Equal(sequenceNow, accountId);
    }
}
