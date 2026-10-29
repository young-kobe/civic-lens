using CivicLens.Application;
using CivicLens.Application.Analysis;
using CivicLens.Application.Statements;
using CivicLens.Host.Analysis;
using CivicLens.Infrastructure.Analysis;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Statements;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CivicLens.Host.Statements;

internal static class StatementDraftComposition
{
    public static AnalysisComposition<StatementDraftRun> Create(IDbContextFactory<CollectionAttemptDbContext> contexts, string connectionString, WorkerBeat beat, ILoggerFactory loggers)
    {
        var status = new PostgresAnalysisStatusStore(contexts, StatementDraftTask.Task);
        var configuration = StatementDraftConfiguration.FromEnvironment();
        if (configuration is null) return AnalysisComposition<StatementDraftRun>.Disabled(status);
        var wakeup = PostgresAnalysisWakeup.FromConnectionString(connectionString, StatementAnalysisListing.Table,
            StatementDraftTask.Task);
        var step = new StatementDraftStep(new PostgresStatementDraftStore(contexts),
            new AnthropicStatementDraftModel(configuration.ApiKey), configuration.Settings);
        return new(status, configuration.Settings.DailyTokenLimit, new AnalysisWorker<StatementDraftRun>(step, status, TimeProvider.System, loggers.CreateLogger<AnalysisWorker<StatementDraftRun>>(), wakeup, beat: beat), wakeup);
    }
}
