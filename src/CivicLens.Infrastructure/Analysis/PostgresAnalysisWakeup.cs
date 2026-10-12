using CivicLens.Application;

namespace CivicLens.Infrastructure.Analysis;

public static class PostgresAnalysisWakeup
{
    private const string Channel = "civic_lens_analysis";

    public static IWorkerWakeup FromConnectionString(string connectionString, string table, string task)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentException.ThrowIfNullOrWhiteSpace(table);
        ArgumentException.ThrowIfNullOrWhiteSpace(task);
        if (!table.All(character => character is >= 'a' and <= 'z' or '_'))
            throw new ArgumentException("The queue table name must hold only lowercase letters and underscores.", nameof(table));
        if (!task.All(character => character is >= 'a' and <= 'z' or >= '0' and <= '9' or '-'))
            throw new ArgumentException("The queue task must hold only lowercase letters, digits, and hyphens.", nameof(task));
        return new PostgresDeadlineWakeup(connectionString, Channel, $"""
            SELECT retry_at AS ticks FROM {table}
             WHERE status IN ('RetryWaiting','WaitingForBudget') AND retry_at IS NOT NULL AND task = '{task}'
            UNION ALL
            SELECT lease_expires_at FROM {table}
             WHERE lease_expires_at IS NOT NULL AND task = '{task}'
            """, $"{table}:{task}");
    }
}
