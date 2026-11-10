namespace Ledgerline.Domain;

// A balanced set of journal lines with one idempotency key (blueprint 6).
//
// INV-01: a journal cannot be built unbalanced. The constructor is private and
// Journal.Create is the only way to get one, so every Journal that exists has
// passed its checks. The posting procedure checks the same rules again (50003
// and 50001), because the database must not trust the application (ADR-006).
//
// INV-02 in the domain: once built, a journal never changes. There is no
// setter and no method that alters it. A mistake is corrected by a new
// reversal journal, never by editing this one.
public sealed class Journal
{
    private Journal(IdempotencyKey key, JournalType type, IReadOnlyList<JournalLine> lines)
    {
        Key = key;
        Type = type;
        Lines = lines;
    }

    public IdempotencyKey Key { get; }

    public JournalType Type { get; }

    public IReadOnlyList<JournalLine> Lines { get; }

    public static Journal Create(IdempotencyKey key, JournalType type, IEnumerable<JournalLine> lines)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(lines);

        // Copy the lines first, so that a caller who changes their own list
        // afterwards cannot change this journal, and check the copy.
        var copy = lines.ToArray();

        if (copy.Any(line => line is null))
        {
            throw new ArgumentException("A journal line cannot be null.", nameof(lines));
        }

        if (copy.Length < 2)
        {
            throw new DomainRuleException(
                DomainRule.JournalTooFewLines,
                $"A journal needs at least two lines; this one has {copy.Length}.");
        }

        // Every currency must net to zero on its own. Adding all the amounts
        // together would accept +100 AED and -100 USD, which is not balanced.
        // The sum is a plain decimal, not Money: a long, balanced journal can
        // pass through a running total larger than DECIMAL(19,4) on its way
        // back to zero.
        foreach (var currencyLines in copy.GroupBy(line => line.Currency))
        {
            var net = currencyLines.Sum(line => line.SignedAmount.Amount);
            if (net != 0m)
            {
                throw new DomainRuleException(
                    DomainRule.JournalNotBalanced,
                    $"INV-01: the {currencyLines.Key.Code} lines of a journal must net to zero; they net to {net}.");
            }
        }

        // Array.AsReadOnly wraps the copy in a ReadOnlyCollection. Callers see
        // only IReadOnlyList, and cannot cast it back to an array or a List to
        // change it: the wrapper is neither.
        return new Journal(key, type, Array.AsReadOnly(copy));
    }
}
