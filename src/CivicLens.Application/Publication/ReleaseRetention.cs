namespace CivicLens.Application.Publication;

public static class ReleaseRetention
{
    public const int NewestKept = 30;
    public const int DailyDaysKept = 7;

    public static DateTimeOffset DailyWindowStart(DateTimeOffset now) =>
        new(now.UtcDateTime.Date.AddDays(1 - DailyDaysKept), TimeSpan.Zero);

    public static ReleaseRetentionPlan Plan(IEnumerable<(int Number, DateTimeOffset PublishedAt)> releases, DateTimeOffset now)
    {
        var distinct = releases.DistinctBy(release => release.Number).ToList();
        var kept = distinct.OrderByDescending(release => release.Number).Take(NewestKept).Select(release => release.Number).ToHashSet();
        var windowStart = DailyWindowStart(now);
        var newestOfEachDay = distinct
            .Where(release => release.PublishedAt >= windowStart)
            .GroupBy(release => release.PublishedAt.UtcDateTime.Date)
            .Select(day => day.Max(release => release.Number));
        kept.UnionWith(newestOfEachDay);
        return new ReleaseRetentionPlan(distinct.Select(release => release.Number).DefaultIfEmpty().Max(), kept);
    }
}
