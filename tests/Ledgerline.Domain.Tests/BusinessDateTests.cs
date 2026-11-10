using System.Globalization;
using System.Reflection;

namespace Ledgerline.Domain.Tests;

// The bank's working day (blueprint 6, INV-08).
public class BusinessDateTests
{
    [Theory]
    [InlineData("2026-11-10", "2026-11-11")]
    [InlineData("2026-11-30", "2026-12-01")] // month end
    [InlineData("2026-12-31", "2027-01-01")] // year end
    [InlineData("2028-02-28", "2028-02-29")] // 2028 is a leap year
    [InlineData("2027-02-28", "2027-03-01")] // 2027 is not
    public void Next_is_the_next_calendar_day(string date, string expected)
    {
        Assert.Equal(Parse(expected), Parse(date).Next());
    }

    [Fact]
    public void Both_constructors_give_the_same_date()
    {
        Assert.Equal(new BusinessDate(new DateOnly(2026, 11, 10)), new BusinessDate(2026, 11, 10));
    }

    [Fact]
    public void Dates_are_ordered()
    {
        var earlier = new BusinessDate(2026, 11, 10);
        var later = new BusinessDate(2026, 11, 11);

        Assert.True(earlier < later);
        Assert.True(later > earlier);
        Assert.True(earlier <= later);
        Assert.True(earlier <= new BusinessDate(2026, 11, 10));
        Assert.True(later >= earlier);
        Assert.False(earlier > later);
        Assert.True(earlier.CompareTo(later) < 0);
        Assert.Equal(0, earlier.CompareTo(new BusinessDate(2026, 11, 10)));

        BusinessDate[] dates = [later, earlier];
        Assert.Equal(new[] { earlier, later }, dates.Order());
    }

    [Fact]
    public void ToString_is_yyyy_mm_dd()
    {
        Assert.Equal("2026-11-10", new BusinessDate(2026, 11, 10).ToString());
    }

    // Under ar-SA the default formatting is on the Hijri calendar, so the
    // year would read 1448, not 2026.
    [Fact]
    public void ToString_stays_gregorian_under_ar_SA()
    {
        var date = new BusinessDate(2026, 11, 10);
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ar-SA");

            Assert.Equal("2026-11-10", date.ToString());

            // Proves the culture really changes the default text; without
            // this the test could pass on a machine where it never mattered.
            Assert.DoesNotContain("2026", date.Value.ToString("yyyy-MM-dd", CultureInfo.CurrentCulture));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    // INV-08: the business date comes from the calendar, never from a clock.
    // So the type offers no door to the clock: no static property such as
    // Today, and no parameterless static method, that hands out a date.
    [Fact]
    public void No_static_member_hands_out_a_date_from_nowhere()
    {
        const BindingFlags publicStatic = BindingFlags.Public | BindingFlags.Static;

        var properties = typeof(BusinessDate).GetProperties(publicStatic)
            .Where(property => property.PropertyType == typeof(BusinessDate))
            .Select(property => property.Name);

        var methods = typeof(BusinessDate).GetMethods(publicStatic)
            .Where(method => method.ReturnType == typeof(BusinessDate) && method.GetParameters().Length == 0)
            .Select(method => method.Name);

        Assert.Empty(properties.Concat(methods));
    }

    private static BusinessDate Parse(string date) =>
        new(DateOnly.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture));
}
