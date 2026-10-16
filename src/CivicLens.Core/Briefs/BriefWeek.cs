using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace CivicLens.Core.Briefs;

public static class BriefWeek
{
    public const string DateFormat = "yyyy-MM-dd";

    public static string RecordIdOf(DateOnly weekStart)
    {
        RequireMonday(weekStart);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"brief|{weekStart.ToString(DateFormat, CultureInfo.InvariantCulture)}")));
    }

    public static IEnumerable<DateOnly> ClosedWeeks(DateOnly firstWeekStart, DateOnly currentWeekStart)
    {
        RequireMonday(firstWeekStart);
        RequireMonday(currentWeekStart);
        for (var week = firstWeekStart; week < currentWeekStart; week = week.AddDays(7)) yield return week;
    }

    public static void RequireMonday(DateOnly weekStart)
    {
        if (weekStart.DayOfWeek != DayOfWeek.Monday)
            throw new ArgumentException($"A brief week starts on a Monday, but {weekStart.ToString(DateFormat, CultureInfo.InvariantCulture)} is not one.", nameof(weekStart));
    }
}
