using System.Reflection;

namespace Ledgerline.Domain.Tests;

// INV-01: a journal cannot be built unbalanced. INV-02 in the domain: once
// built, it cannot be changed.
public class JournalTests
{
    private static readonly LedgerAccountId Cash = new(1);
    private static readonly LedgerAccountId Customer = new(2);
    private static readonly LedgerAccountId FeeIncome = new(3);

    [Fact]
    public void A_balanced_journal_is_built()
    {
        var key = new IdempotencyKey("SAL-1-1");
        JournalLine[] lines = [Debit(Cash, 100.00m), Credit(Customer, 100.00m)];

        var journal = Journal.Create(key, JournalType.SalaryCredit, lines);

        Assert.Equal(key, journal.Key);
        Assert.Equal(JournalType.SalaryCredit, journal.Type);
        Assert.Equal(lines, journal.Lines);
    }

    // Dr customer 105.00 / Cr cash 100.00 / Cr fee income 5.00.
    [Fact]
    public void A_balanced_journal_of_three_lines_is_built()
    {
        var journal = Journal.Create(Key(), JournalType.PaymentOut,
            [Debit(Customer, 105.00m), Credit(Cash, 100.00m), Credit(FeeIncome, 5.00m)]);

        Assert.Equal(3, journal.Lines.Count);
    }

    [Fact]
    public void An_unbalanced_journal_is_refused()
    {
        var error = Assert.Throws<DomainRuleException>(() =>
            Journal.Create(Key(), JournalType.Transfer, [Debit(Cash, 100.00m), Credit(Customer, 99.99m)]));

        Assert.Equal(DomainRule.JournalNotBalanced, error.Rule);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Fewer_than_two_lines_are_refused(int count)
    {
        var lines = Enumerable.Repeat(Debit(Cash, 1.00m), count);

        var error = Assert.Throws<DomainRuleException>(() => Journal.Create(Key(), JournalType.Fee, lines));

        Assert.Equal(DomainRule.JournalTooFewLines, error.Rule);
    }

    // Each currency must net to zero on its own: +100 AED and -100 USD add up
    // to zero as numbers, but the journal is not balanced.
    [Fact]
    public void Lines_that_balance_only_across_currencies_are_refused()
    {
        var error = Assert.Throws<DomainRuleException>(() => Journal.Create(Key(), JournalType.Transfer,
        [
            JournalLine.Debit(Cash, new Money(100.00m, Currency.Aed)),
            JournalLine.Credit(Customer, new Money(100.00m, Currency.Usd)),
        ]));

        Assert.Equal(DomainRule.JournalNotBalanced, error.Rule);
    }

    // The domain allows a journal in two currencies if each balances; it is
    // the database (ck_entry_currency_code) that keeps v1 to AED.
    [Fact]
    public void Two_currencies_that_each_balance_are_accepted()
    {
        var journal = Journal.Create(Key(), JournalType.Transfer,
        [
            JournalLine.Debit(Cash, new Money(100.00m, Currency.Aed)),
            JournalLine.Credit(Customer, new Money(100.00m, Currency.Aed)),
            JournalLine.Debit(Cash, new Money(27.23m, Currency.Usd)),
            JournalLine.Credit(Customer, new Money(27.23m, Currency.Usd)),
        ]);

        Assert.Equal(4, journal.Lines.Count);
    }

    // The database allows it, so the domain does too.
    [Fact]
    public void The_same_account_may_appear_on_several_lines()
    {
        var journal = Journal.Create(Key(), JournalType.Transfer,
            [Debit(Cash, 60.00m), Debit(Cash, 40.00m), Credit(Customer, 100.00m)]);

        Assert.Equal(2, journal.Lines.Count(line => line.LedgerAccountId == Cash));
    }

    // The journal keeps its own copy: changing the list it was built from
    // changes nothing.
    [Fact]
    public void Changing_the_source_list_afterwards_does_not_change_the_journal()
    {
        var source = new List<JournalLine> { Debit(Cash, 100.00m), Credit(Customer, 100.00m) };
        var journal = Journal.Create(Key(), JournalType.Transfer, source);

        source.Add(Debit(Cash, 999.00m));
        source[0] = Debit(Cash, 1.00m);

        Assert.Equal(2, journal.Lines.Count);
        Assert.Equal(Debit(Cash, 100.00m), journal.Lines[0]);
    }

    // Lines cannot be cast back to something that can be changed.
    [Fact]
    public void Lines_cannot_be_cast_to_a_list_or_an_array()
    {
        var journal = Journal.Create(Key(), JournalType.Transfer, [Debit(Cash, 1.00m), Credit(Customer, 1.00m)]);

        Assert.False(journal.Lines is List<JournalLine>);
        Assert.False(journal.Lines is JournalLine[]);

        // It may be an IList, but a read-only one.
        if (journal.Lines is IList<JournalLine> list)
        {
            Assert.True(list.IsReadOnly);
            Assert.Throws<NotSupportedException>(() => list[0] = Debit(Cash, 2.00m));
        }
    }

    // INV-02: nothing on a journal or a line can be changed after it is
    // built. No public setter (init counts as one: `with` could use it), no
    // public field that is not read-only, and no public instance method that
    // returns void, which is how a method that changes state usually looks.
    [Theory]
    [InlineData(typeof(Journal))]
    [InlineData(typeof(JournalLine))]
    public void Journals_and_lines_have_no_way_to_change(Type type)
    {
        const BindingFlags publicInstance = BindingFlags.Public | BindingFlags.Instance;

        var setters = type.GetProperties(publicInstance)
            .Where(property => property.GetSetMethod() is not null)
            .Select(property => "setter " + property.Name);

        var fields = type.GetFields(publicInstance)
            .Where(field => !field.IsInitOnly)
            .Select(field => "field " + field.Name);

        var voidMethods = type.GetMethods(publicInstance)
            .Where(method => method.ReturnType == typeof(void))
            .Select(method => "method " + method.Name);

        Assert.Empty(setters.Concat(fields).Concat(voidMethods));
        Assert.NotEmpty(type.GetProperties(publicInstance)); // the search sees the type's properties
    }

    [Fact]
    public void Null_arguments_are_refused()
    {
        JournalLine[] lines = [Debit(Cash, 1.00m), Credit(Customer, 1.00m)];

        Assert.Throws<ArgumentNullException>(() => Journal.Create(null!, JournalType.Fee, lines));
        Assert.Throws<ArgumentNullException>(() => Journal.Create(Key(), JournalType.Fee, null!));
        Assert.Throws<ArgumentException>(() => Journal.Create(Key(), JournalType.Fee, [lines[0], lines[1], null!]));
    }

    private static IdempotencyKey Key() => new("TEST-" + Guid.NewGuid());

    private static JournalLine Debit(LedgerAccountId account, decimal amount) =>
        JournalLine.Debit(account, new Money(amount, Currency.Aed));

    private static JournalLine Credit(LedgerAccountId account, decimal amount) =>
        JournalLine.Credit(account, new Money(amount, Currency.Aed));
}
