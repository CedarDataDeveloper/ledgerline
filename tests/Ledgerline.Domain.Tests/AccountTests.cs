namespace Ledgerline.Domain.Tests;

// INV-04 (no overdraft) and INV-05 (frozen and closed accounts) as the
// account decides them. The refusals come in the posting procedure's order:
// closed, then frozen, then not enough money.
public class AccountTests
{
    [Theory]
    [InlineData("99.99", DebitDecision.Allowed)]            // below the balance
    [InlineData("100.00", DebitDecision.Allowed)]           // exactly the balance: down to zero is fine
    [InlineData("100.01", DebitDecision.InsufficientFunds)] // one fils too much
    public void An_open_account_can_be_debited_down_to_zero(string amount, DebitDecision expected)
    {
        var account = NewAccount(AccountStatus.Open);

        Assert.Equal(expected, account.CheckDebit(Aed(amount), Aed("100.00")));
    }

    // A balance already below zero cannot cover any debit.
    [Fact]
    public void A_negative_balance_is_insufficient()
    {
        Assert.Equal(DebitDecision.InsufficientFunds, NewAccount(AccountStatus.Open).CheckDebit(Aed("0.01"), Aed("-5.00")));
    }

    // BR-07: a frozen account cannot be debited but still receives a salary.
    [Fact]
    public void A_frozen_account_refuses_debits_and_accepts_credits()
    {
        var account = NewAccount(AccountStatus.Frozen);

        Assert.Equal(DebitDecision.AccountFrozen, account.CheckDebit(Aed("1.00"), Aed("100.00")));
        Assert.Equal(CreditDecision.Allowed, account.CheckCredit(Aed("1.00")));
    }

    [Fact]
    public void A_closed_account_refuses_debits_and_credits()
    {
        var account = NewAccount(AccountStatus.Closed);

        Assert.Equal(DebitDecision.AccountClosed, account.CheckDebit(Aed("1.00"), Aed("100.00")));
        Assert.Equal(CreditDecision.AccountClosed, account.CheckCredit(Aed("1.00")));
    }

    [Fact]
    public void An_open_account_accepts_credits()
    {
        Assert.Equal(CreditDecision.Allowed, NewAccount(AccountStatus.Open).CheckCredit(Aed("1.00")));
    }

    // Two reasons to refuse: the status wins, as in the procedure, which
    // checks status (50008, 50007) before the balance (50006).
    [Theory]
    [InlineData(AccountStatus.Frozen, DebitDecision.AccountFrozen)]
    [InlineData(AccountStatus.Closed, DebitDecision.AccountClosed)]
    public void Status_is_reported_before_insufficient_funds(AccountStatus status, DebitDecision expected)
    {
        Assert.Equal(expected, NewAccount(status).CheckDebit(Aed("500.00"), Aed("100.00")));
    }

    // A wrong currency or a non-positive amount is a mistake in the calling
    // code, not a business answer.
    [Fact]
    public void A_wrong_currency_is_a_programming_error()
    {
        var account = NewAccount(AccountStatus.Open);
        var dollars = new Money(1.00m, Currency.Usd);

        Assert.Throws<ArgumentException>(() => account.CheckDebit(dollars, Aed("100.00")));
        Assert.Throws<ArgumentException>(() => account.CheckDebit(Aed("1.00"), new Money(100.00m, Currency.Usd)));
        Assert.Throws<ArgumentException>(() => account.CheckCredit(dollars));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1.00")]
    public void A_zero_or_negative_amount_is_a_programming_error(string amount)
    {
        var account = NewAccount(AccountStatus.Open);

        Assert.Throws<ArgumentException>(() => account.CheckDebit(Aed(amount), Aed("100.00")));
        Assert.Throws<ArgumentException>(() => account.CheckCredit(Aed(amount)));
    }

    // CanDebit and CanCredit are the yes-or-no form of the Check methods.
    [Theory]
    [InlineData(AccountStatus.Open, "50.00")]
    [InlineData(AccountStatus.Open, "150.00")]
    [InlineData(AccountStatus.Frozen, "50.00")]
    [InlineData(AccountStatus.Closed, "50.00")]
    public void CanDebit_and_CanCredit_agree_with_the_checks(AccountStatus status, string amount)
    {
        var account = NewAccount(status);

        Assert.Equal(account.CheckDebit(Aed(amount), Aed("100.00")) == DebitDecision.Allowed, account.CanDebit(Aed(amount), Aed("100.00")));
        Assert.Equal(account.CheckCredit(Aed(amount)) == CreditDecision.Allowed, account.CanCredit(Aed(amount)));
    }

    [Fact]
    public void The_constructor_keeps_its_values_and_needs_a_product_code()
    {
        var account = new Account(new AccountId(1000000001), "SAVINGS_AED", Currency.Aed, AccountStatus.Frozen);

        Assert.Equal(new AccountId(1000000001), account.Id);
        Assert.Equal("SAVINGS_AED", account.ProductCode);
        Assert.Equal(Currency.Aed, account.Currency);
        Assert.Equal(AccountStatus.Frozen, account.Status);

        Assert.Throws<ArgumentException>(() => new Account(new AccountId(1), "", Currency.Aed, AccountStatus.Open));
    }

    private static Account NewAccount(AccountStatus status) =>
        new(new AccountId(1000000001), "SAVINGS_AED", Currency.Aed, status);

    private static Money Aed(string amount) =>
        new(decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture), Currency.Aed);
}
