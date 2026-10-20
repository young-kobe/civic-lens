using CivicLens.Application;

namespace CivicLens.Infrastructure.Publication;

public static class PostgresFactPublishingWakeup
{
    private const string Channel = "civic_lens_fact_publishing";

    private static readonly string NextHourTicks = DatabaseClock.TicksOfSql("(date_trunc('hour', clock_timestamp()) + interval '1 hour')");

    public static IWorkerWakeup FromConnectionString(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        return new PostgresDeadlineWakeup(connectionString, Channel, $"SELECT {NextHourTicks} AS ticks");
    }
}
