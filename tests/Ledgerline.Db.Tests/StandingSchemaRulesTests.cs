namespace Ledgerline.Db.Tests;

// Rules that hold for every user table in BankCore, now and in every later
// migration. They read the catalog of a database migrated with all the real
// scripts, so a future table that breaks a rule fails here.
public class StandingSchemaRulesTests(BankCoreDatabase db) : IClassFixture<BankCoreDatabase>
{
    // Every user-table column as schema.table.column, with its type name.
    // TYPE_NAME(system_type_id) sees through alias types to the base type.
    private const string ColumnsSql =
        """
        SELECT s.name + '.' + t.name + '.' + c.name + '|' + TYPE_NAME(c.system_type_id)
        FROM sys.columns AS c
        JOIN sys.tables  AS t ON t.object_id = c.object_id
        JOIN sys.schemas AS s ON s.schema_id = t.schema_id
        WHERE t.is_ms_shipped = 0;
        """;

    // Column names that hold personal data, matched at the start of the name
    // so national_id_synthetic or phone_mobile count too.
    private static readonly string[] PersonalDataNames =
    [
        "full_name", "first_name", "last_name", "date_of_birth",
        "national_id", "passport", "phone", "email", "address",
    ];

    // Personal data lives only in the pii schema (blueprint 10, ADR-008).
    // The IBAN stays in core.account by design: an account needs it, and
    // change data capture will leave it out of the captured columns (LL-011).
    [Fact]
    public async Task No_personal_data_column_outside_the_pii_schema()
    {
        var personal = (await ColumnsAsync())
            .Where(column => PersonalDataNames.Any(name => column.Column.StartsWith(name, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        var outsidePii = personal.Where(column => column.Schema != "pii").Select(column => column.FullName);
        Assert.Empty(outsidePii);

        // Proves the name matching works: it finds the columns that rightly
        // live in pii. Without this, a broken match would pass forever.
        Assert.Contains("pii.customer_identity.full_name", personal.Select(column => column.FullName));
    }

    // Money is DECIMAL(19,4). Approximate and fixed-scale money types are
    // never used (CLAUDE.md non-negotiables).
    [Fact]
    public async Task No_float_real_money_or_smallmoney_column()
    {
        string[] forbidden = ["float", "real", "money", "smallmoney"];

        var offending = (await ColumnsAsync())
            .Where(column => forbidden.Contains(column.Type))
            .Select(column => $"{column.FullName} ({column.Type})");

        Assert.Empty(offending);
    }

    // Balances are derived from the ledger through an indexed view, never
    // stored as a running total (blueprint 7.4). A column named balance, or
    // ending in _balance, would be one. This still allows balance_basis in
    // the interest accrual table (LL-008): that is a memo of the balance one
    // day's accrual was calculated on, not a balance anyone reads back.
    [Fact]
    public async Task No_running_balance_column()
    {
        var offending = (await ColumnsAsync())
            .Where(column => column.Column.Equals("balance", StringComparison.OrdinalIgnoreCase)
                || column.Column.EndsWith("_balance", StringComparison.OrdinalIgnoreCase))
            .Select(column => column.FullName);

        Assert.Empty(offending);
    }

    private sealed record ColumnInfo(string Schema, string Column, string FullName, string Type);

    private async Task<List<ColumnInfo>> ColumnsAsync()
    {
        var rows = await db.StringsAsync(ColumnsSql);

        return rows.Select(row =>
        {
            var parts = row.Split('|');
            var name = parts[0].Split('.');
            return new ColumnInfo(name[0], name[2], parts[0], parts[1]);
        }).ToList();
    }
}
