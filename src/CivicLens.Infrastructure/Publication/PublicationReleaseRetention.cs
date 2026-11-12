using CivicLens.Application.Publication;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Publication.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.Publication;

internal static class PublicationReleaseRetention
{
    public static async Task<ReleaseRetentionPlan> ReadAsync(
        CollectionAttemptDbContext db, CancellationToken cancellationToken)
    {
        var releases = db.Set<PublicationReleaseRow>().AsNoTracking();
        var now = (await DatabaseClock.ReadAsync(db, cancellationToken)).Offset;
        var windowStartTicks = ReleaseRetention.DailyWindowStart(now).Ticks;
        var newest = await releases.OrderByDescending(row => row.ReleaseNumber).Take(ReleaseRetention.NewestKept)
            .Select(row => new { row.ReleaseNumber, row.PublishedAtUtcTicks }).ToListAsync(cancellationToken);
        var newestOfEachDay = await releases.Where(row => row.PublishedAtUtcTicks >= windowStartTicks)
            .GroupBy(row => row.PublishedAtUtcTicks / TimeSpan.TicksPerDay)
            .Select(day => new { ReleaseNumber = day.Max(row => row.ReleaseNumber), PublishedAtUtcTicks = day.Max(row => row.PublishedAtUtcTicks) })
            .ToListAsync(cancellationToken);
        var plan = ReleaseRetention.Plan([.. newest.Concat(newestOfEachDay).Select(row => (row.ReleaseNumber,
            new DateTimeOffset(row.PublishedAtUtcTicks, TimeSpan.Zero)))], now);
        return plan;
    }
}
