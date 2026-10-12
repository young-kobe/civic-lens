using CivicLens.Application;

namespace CivicLens.Infrastructure.Statements;

public static class PostgresStatementAnalysisWakeup
{
    private const string Channel = "civic_lens_analysis";
    private const string Deadlines = """
        SELECT retry_at AS ticks FROM statement_analysis
         WHERE status IN ('RetryWaiting','WaitingForBudget') AND retry_at IS NOT NULL
        UNION ALL
        SELECT lease_expires_at FROM statement_analysis
         WHERE lease_expires_at IS NOT NULL
        """;

    public static IWorkerWakeup FromConnectionString(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        return new PostgresDeadlineWakeup(connectionString, Channel, Deadlines);
    }
}
