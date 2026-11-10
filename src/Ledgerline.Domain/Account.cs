namespace Ledgerline.Domain;

// A customer account: its product, currency and status (blueprint 6). It
// decides whether a debit or a credit is allowed (INV-04, INV-05).
//
// The account holds no balance. Balances are derived from the ledger, never
// stored (ADR-007), so the caller reads the balance and passes it in.
//
// These checks are an early answer, so the application can refuse quickly and
// clearly. The balance read may already be stale when the posting arrives;
// the judge is the posting procedure, which checks the same rules again while
// the account is locked (ADR-021).
public sealed class Account
{
    public Account(AccountId id, string productCode, Currency currency, AccountStatus status)
    {
        if (string.IsNullOrWhiteSpace(productCode))
        {
            throw new ArgumentException("An account needs a product code.", nameof(productCode));
        }

        ArgumentNullException.ThrowIfNull(currency);

        Id = id;
        ProductCode = productCode;
        Currency = currency;
        Status = status;
    }

    public AccountId Id { get; }

    public string ProductCode { get; }

    public Currency Currency { get; }

    public AccountStatus Status { get; }

    // The refusals come in the same order as in the posting procedure:
    // closed first (50008), then frozen (50007), then not enough money
    // (50006). So a frozen account with too little money is reported as
    // frozen, the same answer the database would give.
    public DebitDecision CheckDebit(Money amount, Money balance)
    {
        EnsurePositiveInAccountCurrency(amount, nameof(amount));
        EnsureAccountCurrency(balance, nameof(balance));

        if (Status == AccountStatus.Closed)
        {
            return DebitDecision.AccountClosed;
        }

        if (Status == AccountStatus.Frozen)
        {
            return DebitDecision.AccountFrozen;
        }

        // INV-04: the balance may reach exactly zero but never go below it. A
        // balance that is already negative cannot cover any debit.
        if (amount > balance)
        {
            return DebitDecision.InsufficientFunds;
        }

        return DebitDecision.Allowed;
    }

    // Only a closed account refuses a credit. A frozen account still receives
    // money, such as a salary (BR-07).
    public CreditDecision CheckCredit(Money amount)
    {
        EnsurePositiveInAccountCurrency(amount, nameof(amount));

        return Status == AccountStatus.Closed ? CreditDecision.AccountClosed : CreditDecision.Allowed;
    }

    public bool CanDebit(Money amount, Money balance) => CheckDebit(amount, balance) == DebitDecision.Allowed;

    public bool CanCredit(Money amount) => CheckCredit(amount) == CreditDecision.Allowed;

    // A wrong currency or a zero or negative amount is a mistake in the
    // calling code, not a business answer, so it throws instead of returning
    // a decision.
    private void EnsurePositiveInAccountCurrency(Money amount, string parameterName)
    {
        EnsureAccountCurrency(amount, parameterName);

        if (!amount.IsPositive)
        {
            throw new ArgumentException($"The amount must be positive; got {amount}.", parameterName);
        }
    }

    private void EnsureAccountCurrency(Money money, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(money, parameterName);

        if (money.Currency != Currency)
        {
            throw new ArgumentException(
                $"Account {Id.Value} is in {Currency.Code}, not {money.Currency.Code}.", parameterName);
        }
    }
}
