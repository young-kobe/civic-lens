using CivicLens.Application;
using CivicLens.Application.Analysis;
using CivicLens.Application.Statements;
using CivicLens.Infrastructure.Analysis;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Statements;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Host.Statements;

internal sealed class StatementDraftComposition
{
    private readonly IAnalysisStatusStore status;
    private readonly long? dailyTokenLimit;

    private StatementDraftComposition(IAnalysisStatusStore status, StatementDraftConfiguration? configuration,
        AnalysisWorker<StatementDraftRun>? worker, IWorkerWakeup? wakeup)
    {
        this.status = status;
        dailyTokenLimit = configuration?.Settings.DailyTokenLimit;
        Worker = worker;
        Wakeup = wakeup;
    }

    public AnalysisWorker<StatementDraftRun>? Worker { get; }
    public IWorkerWakeup? Wakeup { get; }

    public static StatementDraftComposition Create(IDbContextFactory<CollectionAttemptDbContext> contexts, string connectionString)
    {
        var status = new PostgresAnalysisStatusStore(contexts, StatementDraftTask.Task);
        var configuration = StatementDraftConfiguration.FromEnvironment();
        if (configuration is null) return new(status, null, null, null);
        var wakeup = PostgresAnalysisWakeup.FromConnectionString(connectionString, PostgresStatementAnalysisStore.Table,
            StatementDraftTask.Task);
        var step = new StatementDraftingStep(new PostgresStatementDraftStore(contexts),
            new AnthropicStatementDraftingModel(configuration.ApiKey), configuration.Settings);
        return new(status, configuration, new AnalysisWorker<StatementDraftRun>(step, status, TimeProvider.System, wakeup), wakeup);
    }

    public Task RecordStartAsync(CancellationToken cancellationToken) => status.RecordStartAsync(dailyTokenLimit, cancellationToken);
}
