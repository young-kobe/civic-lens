using CivicLens.Application;

namespace CivicLens.Infrastructure.Reports;

public static class PostgresReportPullWakeup
{
    private const string Channel = "civic_lens_report_pull";
    private const int IntervalSeconds = 300;

    private static readonly string NextIntervalTicks = DatabaseClock.TicksOfSql(
        $"to_timestamp(floor(EXTRACT(EPOCH FROM clock_timestamp()) / {IntervalSeconds}) * {IntervalSeconds} + {IntervalSeconds})");

    public static IWorkerWakeup FromConnectionString(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        return new PostgresDeadlineWakeup(connectionString, Channel, $"SELECT {NextIntervalTicks} AS ticks");
    }
}
