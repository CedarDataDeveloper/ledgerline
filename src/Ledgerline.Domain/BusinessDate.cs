using System.Globalization;

namespace Ledgerline.Domain;

// The bank's working day (blueprint 6 and 7.5). Postings carry the business
// date that is open in ops.business_calendar, not the date on a clock (INV-08).
//
// The bank's day is Asia/Dubai time: UTC+4, with no daylight saving. Turning a
// clock reading into a business date is not this type's job; that arrives
// with the injected clock in LL-006. On purpose, this type has no member that
// reads the clock, such as a Today property, and a test checks that.
public readonly record struct BusinessDate : IComparable<BusinessDate>
{
    public BusinessDate(DateOnly value)
    {
        Value = value;
    }

    public BusinessDate(int year, int month, int day)
        : this(new DateOnly(year, month, day))
    {
    }

    public DateOnly Value { get; }

    // The next calendar day. The bank runs end of day every calendar day,
    // weekends and holidays included, so there are no days to skip.
    public BusinessDate Next() => new(Value.AddDays(1));

    public int CompareTo(BusinessDate other) => Value.CompareTo(other.Value);

    public static bool operator <(BusinessDate left, BusinessDate right) => left.CompareTo(right) < 0;

    public static bool operator >(BusinessDate left, BusinessDate right) => left.CompareTo(right) > 0;

    public static bool operator <=(BusinessDate left, BusinessDate right) => left.CompareTo(right) <= 0;

    public static bool operator >=(BusinessDate left, BusinessDate right) => left.CompareTo(right) >= 0;

    // Always yyyy-MM-dd on the Gregorian calendar. Under the ar-SA culture
    // the default formatting would give a Hijri date such as 1448-05-29.
    public override string ToString() => Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
