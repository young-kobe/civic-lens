using CivicLens.Application.Analysis;
using CivicLens.Application.Statements;
using CivicLens.Host.Analysis;
using CivicLens.Infrastructure.Analysis;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Statements;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Host.Statements;

internal static class StatementGateComposition
{
    public const string IssuesUnsetReason = "issuesConfigurationUnset";

    public static AnalysisComposition<StatementGateRun> Create(IDbContextFactory<CollectionAttemptDbContext> contexts, string connectionString)
    {
        var status = new PostgresAnalysisStatusStore(contexts, StatementGateTask.Task);
        var configuration = StatementGateConfiguration.FromEnvironment();
        if (configuration is null) return AnalysisComposition<StatementGateRun>.Disabled(status);
        var rubric = StatementIssueRubric.FromEnvironment();
        if (rubric is null) return new(status, configuration.Settings.DailyTokenLimit, null, null, IssuesUnsetReason);
        var wakeup = PostgresAnalysisWakeup.FromConnectionString(connectionString, StatementAnalysisListing.Table,
            StatementGateTask.Task);
        var step = new StatementGateStep(new PostgresStatementGateStore(contexts),
            new AnthropicStatementGateModel(configuration.ApiKey), configuration.Settings, rubric);
        var worker = new AnalysisWorker<StatementGateRun>(step, status, TimeProvider.System, wakeup);
        return new(status, configuration.Settings.DailyTokenLimit, worker, wakeup);
    }
}
