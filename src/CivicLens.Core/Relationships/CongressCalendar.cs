namespace CivicLens.Core.Relationships;

public static class CongressCalendar
{
    private const int FirstCongressYear = 1789;
    private const int FirstDayOfJanuaryTerm = 3;
    private static readonly DateOnly FirstJanuaryTermStart = new(1935, 1, FirstDayOfJanuaryTerm);

    public static DateOnly StartOf(int congress) => new(FirstCongressYear + 2 * (congress - 1), 1, FirstDayOfJanuaryTerm);

    public static int? CongressOn(DateOnly date)
    {
        if (date < FirstJanuaryTermStart) return null;
        var termYear = date.Month == 1 && date.Day < FirstDayOfJanuaryTerm ? date.Year - 1 : date.Year;
        return (termYear - FirstCongressYear) / 2 + 1;
    }
}
