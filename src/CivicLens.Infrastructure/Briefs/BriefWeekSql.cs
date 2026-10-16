namespace CivicLens.Infrastructure.Briefs;

internal static class BriefWeekSql
{
    private const string Zone = "'America/New_York'";

    public const string CurrentWeekStart = $"date_trunc('week', clock_timestamp() AT TIME ZONE {Zone})::date";

    public static readonly string NextBoundaryTicks = DatabaseClock.TicksOfSql(
        $"((date_trunc('week', clock_timestamp() AT TIME ZONE {Zone}) + interval '7 days') AT TIME ZONE {Zone})");

    public static string StartTicks(string weekStartDate) => DatabaseClock.TicksOfSql($"(({weekStartDate})::timestamp AT TIME ZONE {Zone})");

    public static string EndTicks(string weekStartDate) => DatabaseClock.TicksOfSql($"((({weekStartDate}) + 7)::timestamp AT TIME ZONE {Zone})");
}
