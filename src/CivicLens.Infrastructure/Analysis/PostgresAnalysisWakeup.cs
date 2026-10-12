using CivicLens.Application;

namespace CivicLens.Infrastructure.Analysis;

public static class PostgresAnalysisWakeup
{
    private const string Channel = "civic_lens_analysis";

    public static IWorkerWakeup FromConnectionString(string connectionString, string table)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentException.ThrowIfNullOrWhiteSpace(table);
        if (!table.All(character => character is >= 'a' and <= 'z' or '_'))
            throw new ArgumentException("The queue table name must hold only lowercase letters and underscores.", nameof(table));
        return new PostgresDeadlineWakeup(connectionString, Channel, $"""
            SELECT retry_at AS ticks FROM {table}
             WHERE status IN ('RetryWaiting','WaitingForBudget') AND retry_at IS NOT NULL
            UNION ALL
            SELECT lease_expires_at FROM {table}
             WHERE lease_expires_at IS NOT NULL
            """);
    }
}
