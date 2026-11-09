namespace Ledgerline.Db.Tests;

// pii.token_vault maps internal IDs to the random tokens the warehouse sees
// (blueprint 10). Triggers give every new customer and account its token, so
// the extract can always swap one for the other.
public class TokenVaultTests(BankCoreDatabase db) : IClassFixture<BankCoreDatabase>
{
    [Fact]
    public async Task A_new_customer_gets_exactly_one_token()
    {
        var customerId = await db.InsertCustomerAsync();

        Assert.Equal(1, await VaultRowsAsync("CUSTOMER", customerId));
    }

    [Fact]
    public async Task A_new_account_gets_exactly_one_token()
    {
        var customerId = await db.InsertCustomerAsync();
        var accountId = await db.OpenAccountAsync(customerId, "SAVINGS_AED");

        Assert.Equal(1, await VaultRowsAsync("ACCOUNT", accountId));
    }

    // A trigger fires once per statement, not once per row, so it must
    // handle every row of a multi-row insert.
    [Fact]
    public async Task A_multi_row_insert_gives_each_customer_its_own_token()
    {
        var tokens = await db.StringsAsync(
            """
            DECLARE @inserted TABLE (customer_id bigint);

            INSERT INTO core.customer (persona_code, home_emirate, kyc_status, onboarded_on, status)
            OUTPUT inserted.customer_id INTO @inserted
            VALUES ('STARTER',  'AUH', 'PENDING', '2026-11-09', 'ACTIVE'),
                   ('BUILDER',  'SHJ', 'PASSED',  '2026-11-09', 'ACTIVE'),
                   ('INVESTOR', 'RAK', 'FAILED',  '2026-11-09', 'ACTIVE');

            SELECT CAST(v.token AS varchar(36))
            FROM pii.token_vault AS v
            JOIN @inserted AS i ON i.customer_id = v.entity_id
            WHERE v.entity_type = 'CUSTOMER';
            """);

        Assert.Equal(3, tokens.Count);
        Assert.Equal(3, tokens.Distinct().Count());
    }

    // The trigger already wrote the customer's row, so a second one for the
    // same entity must be refused by the primary key.
    [Fact]
    public async Task A_second_vault_row_for_the_same_entity_is_refused()
    {
        var customerId = await db.InsertCustomerAsync();

        await BankCoreDatabase.AssertRefusedAsync(
            () => db.ExecuteAsync($"INSERT INTO pii.token_vault (entity_type, entity_id) VALUES ('CUSTOMER', {customerId});"),
            2627, "pk_token_vault");
    }

    private Task<int> VaultRowsAsync(string entityType, long entityId) =>
        db.ScalarAsync<int>(
            $"SELECT COUNT(*) FROM pii.token_vault WHERE entity_type = '{entityType}' AND entity_id = {entityId};");
}
