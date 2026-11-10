namespace Ledgerline.Domain;

// The life of a customer account, as core.account.status stores it
// (ck_account_status). A parity test in Ledgerline.Db.Tests keeps the two
// lists equal.
public enum AccountStatus
{
    Open,
    Frozen,
    Closed,
}

public static class AccountStatusCodes
{
    // The code the database stores, written out for the same reason as
    // JournalTypeCodes.ToCode.
    public static string ToCode(this AccountStatus status) => status switch
    {
        AccountStatus.Open => "OPEN",
        AccountStatus.Frozen => "FROZEN",
        AccountStatus.Closed => "CLOSED",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Not a known account status."),
    };
}
