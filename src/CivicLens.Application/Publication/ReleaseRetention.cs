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
        var today = now.UtcDateTime.Date;
        var newestOfEachDay = distinct
            .Where(release => release.PublishedAt >= windowStart && release.PublishedAt.UtcDateTime.Date <= today)
            .GroupBy(release => release.PublishedAt.UtcDateTime.Date)
            .Select(day => day.Max(release => release.Number));
        kept.UnionWith(newestOfEachDay);
        return new ReleaseRetentionPlan(distinct.Select(release => release.Number).DefaultIfEmpty().Max(), kept);
    }
}

public sealed record ReleaseRetentionPlan(int HighestNumber, IReadOnlySet<int> KeptNumbers)
{
    // A folder above the highest committed number may belong to a release in progress, so it is never retired.
    public bool Retires(int releaseNumber) => releaseNumber <= HighestNumber && !KeptNumbers.Contains(releaseNumber);
}
