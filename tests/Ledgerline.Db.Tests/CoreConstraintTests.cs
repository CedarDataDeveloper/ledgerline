using Ledgerline.Domain;

namespace Ledgerline.Db.Tests;

// The value lists, links and seed data of the core and pii tables
// (blueprint 7.1). Each refusal names the constraint that must refuse it.
public class CoreConstraintTests(BankCoreDatabase db) : IClassFixture<BankCoreDatabase>
{
    // --- Value lists ----------------------------------------------------------
    // Every unknown value fits its column, so a "value too long" error cannot
    // fire before the check constraint.

    [Fact]
    public Task An_unknown_kyc_status_is_refused() =>
        BankCoreDatabase.AssertRefusedAsync(
            () => db.InsertCustomerAsync(kycStatus: "MAYBE"), 547, "ck_customer_kyc_status");

    [Fact]
    public Task An_unknown_persona_is_refused() =>
        BankCoreDatabase.AssertRefusedAsync(
            () => db.InsertCustomerAsync(personaCode: "TOURIST"), 547, "ck_customer_persona_code");

    [Fact]
    public Task An_unknown_emirate_is_refused() =>
        BankCoreDatabase.AssertRefusedAsync(
            () => db.InsertCustomerAsync(homeEmirate: "XXX"), 547, "ck_customer_home_emirate");

    [Fact]
    public Task An_unknown_customer_status_is_refused() =>
        BankCoreDatabase.AssertRefusedAsync(
            () => db.InsertCustomerAsync(status: "DORMANT"), 547, "ck_customer_status");

    [Fact]
    public async Task An_unknown_account_status_is_refused()
    {
        var customerId = await db.InsertCustomerAsync();
        var accountId = await db.NextAccountIdAsync();

        await BankCoreDatabase.AssertRefusedAsync(
            () => db.InsertAccountAsync(accountId, customerId, "SAVINGS_AED", SyntheticIban.Generate(accountId), status: "DORMANT"),
            547, "ck_account_status");
    }

    // v1 is AED only (a later migration widens the list).
    [Fact]
    public async Task An_account_currency_other_than_aed_is_refused()
    {
        var customerId = await db.InsertCustomerAsync();
        var accountId = await db.NextAccountIdAsync();

        await BankCoreDatabase.AssertRefusedAsync(
            () => db.InsertAccountAsync(accountId, customerId, "SAVINGS_AED", SyntheticIban.Generate(accountId), currencyCode: "USD"),
            547, "ck_account_currency_code");
    }

    [Fact]
    public Task An_unknown_product_type_is_refused() =>
        BankCoreDatabase.AssertRefusedAsync(
            () => db.ExecuteAsync(
                "INSERT INTO core.product (product_code, product_type, currency_code, interest_bearing) VALUES ('LOAN_AED', 'LOAN', 'AED', 1);"),
            547, "ck_product_product_type");

    [Fact]
    public Task A_product_currency_other_than_aed_is_refused() =>
        BankCoreDatabase.AssertRefusedAsync(
            () => db.ExecuteAsync(
                "INSERT INTO core.product (product_code, product_type, currency_code, interest_bearing) VALUES ('SAVINGS_USD', 'SAVINGS', 'USD', 1);"),
            547, "ck_product_currency_code");

    [Fact]
    public Task An_unknown_vault_entity_type_is_refused() =>
        BankCoreDatabase.AssertRefusedAsync(
            () => db.ExecuteAsync("INSERT INTO pii.token_vault (entity_type, entity_id) VALUES ('LOAN', 1);"),
            547, "ck_token_vault_entity_type");

    // --- Links ----------------------------------------------------------------

    [Fact]
    public async Task An_account_for_a_missing_customer_is_refused()
    {
        var accountId = await db.NextAccountIdAsync();

        await BankCoreDatabase.AssertRefusedAsync(
            () => db.InsertAccountAsync(accountId, -1, "SAVINGS_AED", SyntheticIban.Generate(accountId)),
            547, "fk_account_customer");
    }

    [Fact]
    public async Task An_account_for_a_missing_product_is_refused()
    {
        var customerId = await db.InsertCustomerAsync();
        var accountId = await db.NextAccountIdAsync();

        await BankCoreDatabase.AssertRefusedAsync(
            () => db.InsertAccountAsync(accountId, customerId, "GOLD_AED", SyntheticIban.Generate(accountId)),
            547, "fk_account_product");
    }

    [Fact]
    public Task An_identity_for_a_missing_customer_is_refused() =>
        BankCoreDatabase.AssertRefusedAsync(
            () => db.ExecuteAsync(
                """
                INSERT INTO pii.customer_identity (customer_id, full_name, date_of_birth, national_id_synthetic)
                VALUES (-1, N'Test Person', '1990-01-01', 'SYN-MISSING-1');
                """),
            547, "fk_customer_identity_customer");

    [Fact]
    public async Task A_second_identity_with_the_same_national_id_is_refused()
    {
        var first = await db.InsertCustomerAsync();
        var second = await db.InsertCustomerAsync();
        await db.ExecuteAsync(
            $"""
            INSERT INTO pii.customer_identity (customer_id, full_name, date_of_birth, national_id_synthetic)
            VALUES ({first}, N'Test Person One', '1990-01-01', 'SYN-DUPLICATE-1');
            """);

        await BankCoreDatabase.AssertRefusedAsync(
            () => db.ExecuteAsync(
                $"""
                INSERT INTO pii.customer_identity (customer_id, full_name, date_of_birth, national_id_synthetic)
                VALUES ({second}, N'Test Person Two', '1991-02-02', 'SYN-DUPLICATE-1');
                """),
            2627, "uq_customer_identity_national_id_synthetic");
    }

    // --- Seed data ------------------------------------------------------------

    [Fact]
    public async Task Product_holds_exactly_the_two_seeded_products()
    {
        var products = await db.StringsAsync(
            """
            SELECT product_code + '|' + product_type + '|' + currency_code + '|' + CAST(interest_bearing AS varchar(1))
            FROM core.product
            ORDER BY product_code;
            """);

        Assert.Equal(new[] { "CURRENT_AED|CURRENT|AED|0", "SAVINGS_AED|SAVINGS|AED|1" }, products);
    }
}
