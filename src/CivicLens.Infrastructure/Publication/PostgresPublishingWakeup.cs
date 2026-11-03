using CivicLens.Application;

namespace CivicLens.Infrastructure.Publication;

public static class PostgresPublishingWakeup
{
    private const string Channel = "civic_lens_publishing";

    /// <summary>Wakes at each full hour, at the deadline of an approval notification, and when the gap after the newest release ends.</summary>
    public static IIntervalWakeup FromConnectionString(string connectionString, TimeSpan releaseGap) =>
        PostgresIntervalWakeup.FromConnectionString(connectionString, Channel, TimeSpan.FromHours(1),
            $"(SELECT max(published_at_utc_ticks) FROM publication_releases) + {releaseGap.Ticks}");
}
