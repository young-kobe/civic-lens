using CivicLens.Application;

namespace CivicLens.Infrastructure;

/// <summary>A wakeup that fires at each multiple of the interval on the database clock. Nothing notifies its channel.</summary>
public static class PostgresIntervalWakeup
{
    public static IWorkerWakeup FromConnectionString(string connectionString, string channel, TimeSpan interval)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentException.ThrowIfNullOrWhiteSpace(channel);
        ArgumentOutOfRangeException.ThrowIfLessThan(interval, TimeSpan.FromSeconds(1));
        var seconds = (long)interval.TotalSeconds;
        var next = DatabaseClock.TicksOfSql($"to_timestamp(floor(EXTRACT(EPOCH FROM clock_timestamp()) / {seconds}) * {seconds} + {seconds})");
        return new PostgresDeadlineWakeup(connectionString, channel, $"SELECT {next} AS ticks");
    }
}
