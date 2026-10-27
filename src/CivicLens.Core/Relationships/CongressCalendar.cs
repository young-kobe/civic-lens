using System.Globalization;

namespace CivicLens.Core.Relationships;

public static class CongressCalendar
{
    private const int FirstCongressYear = 1789;
    private const int FirstDayOfJanuaryTerm = 3;
    private static readonly DateOnly FirstJanuaryTermStart = new(1935, 1, FirstDayOfJanuaryTerm);

    public static DateOnly StartOf(int congress) => new(FirstCongressYear + 2 * (congress - 1), 1, FirstDayOfJanuaryTerm);

    public static int? CongressOn(DateOnly date) =>
        date < FirstJanuaryTermStart ? null : (TermYearOn(date) - FirstCongressYear) / 2 + 1;

    public static int SessionOn(DateOnly date) => (TermYearOn(date) - FirstCongressYear) % 2 + 1;

    public static string Ordinal(int number)
    {
        var suffix = number % 100 is >= 11 and <= 13 ? "th" : (number % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" };
        return number.ToString(CultureInfo.InvariantCulture) + suffix;
    }

    private static int TermYearOn(DateOnly date) => date.Month == 1 && date.Day < FirstDayOfJanuaryTerm ? date.Year - 1 : date.Year;
}
