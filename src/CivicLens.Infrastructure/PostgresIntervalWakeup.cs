using CivicLens.Application;

namespace CivicLens.Infrastructure;

/// <summary>A wakeup that fires at each multiple of the interval on the database clock. A notification whose payload is a deadline in ticks also wakes it at that deadline. An optional query adds more deadlines.</summary>
public static class PostgresIntervalWakeup
{
    public static IWorkerWakeup FromConnectionString(string connectionString, string channel, TimeSpan interval,
        string? extraDeadlineSql = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentException.ThrowIfNullOrWhiteSpace(channel);
        ArgumentOutOfRangeException.ThrowIfLessThan(interval, TimeSpan.FromSeconds(1));
        var seconds = (long)interval.TotalSeconds;
        var next = DatabaseClock.TicksOfSql($"to_timestamp(floor(EXTRACT(EPOCH FROM clock_timestamp()) / {seconds}) * {seconds} + {seconds})");
        return new PostgresDeadlineWakeup(connectionString, channel, $"""
            SELECT {next} AS ticks
            {(extraDeadlineSql is null ? "" : $"UNION ALL SELECT {extraDeadlineSql}")}
            """);
    }
}
