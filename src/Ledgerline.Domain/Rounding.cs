namespace Ledgerline.Domain;

// The one place in C# that rounds money (blueprint 7.5).
//
// Ledgerline rounds half away from zero everywhere: 0.005 becomes 0.01 and
// -0.005 becomes -0.01. SQL Server's ROUND already does this. C#'s
// decimal.Round and Math.Round do not: by default they use banker's rounding
// (half to even), which turns 0.005 into 0.00 and 0.025 into 0.02. Two
// languages with two defaults is a classic source of one-fils differences
// between the application and the database, so every rounding goes through
// here, with the mode written out.
public static class Rounding
{
    public static decimal HalfAwayFromZero(decimal value, int decimalPlaces) =>
        decimal.Round(value, decimalPlaces, MidpointRounding.AwayFromZero);
}
