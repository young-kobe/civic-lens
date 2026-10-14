using CivicLens.Application;
using CivicLens.Application.Actions;
using CivicLens.Application.Analysis;
using CivicLens.Application.Collection.Jobs;
using CivicLens.Application.Collection.Processing;
using CivicLens.Application.Documents;
using CivicLens.Application.Issues;
using CivicLens.Infrastructure.Actions;
using CivicLens.Infrastructure.Analysis;
using CivicLens.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Host.Actions;

internal sealed class ActionDraftComposition
{
    private readonly IAnalysisStatusStore status;
    private readonly long? dailyTokenLimit;

    private ActionDraftComposition(IAnalysisStatusStore status, ActionDraftConfiguration? configuration,
        AnalysisWorker<ActionDraftRun>? worker, IWorkerWakeup? wakeup)
    {
        this.status = status;
        dailyTokenLimit = configuration?.Settings.DailyTokenLimit;
        Worker = worker;
        Wakeup = wakeup;
    }

    public AnalysisWorker<ActionDraftRun>? Worker { get; }
    public IWorkerWakeup? Wakeup { get; }

    public static ActionDraftComposition Create(IDbContextFactory<CollectionAttemptDbContext> contexts, string connectionString,
        ICollectionJobStore jobs, ApiResponseReader responses, IDocumentTextExtractor textExtractor, IssueConfigurationRevision? issues,
        string artifactRoot)
    {
        var status = new PostgresAnalysisStatusStore(contexts, ActionDraftTask.Task);
        var configuration = ActionDraftConfiguration.FromEnvironment();
        if (configuration is null) return new(status, null, null, null);
        if (issues is null) throw new ArgumentException("Action drafting needs CIVIC_LENS_ISSUES_CONFIG.");
        var wakeup = PostgresAnalysisWakeup.FromConnectionString(connectionString, PostgresActionDraftStore.Table, ActionDraftTask.Task);
        var reader = new ActionBillReader(jobs, responses, textExtractor, artifactRoot);
        var step = new ActionDraftingStep(new PostgresActionDraftStore(contexts), reader,
            new AnthropicActionDraftingModel(configuration.ApiKey), configuration.Settings, issues);
        return new(status, configuration, new AnalysisWorker<ActionDraftRun>(step, status, TimeProvider.System, wakeup), wakeup);
    }

    public Task RecordStartAsync(CancellationToken cancellationToken) => status.RecordStartAsync(dailyTokenLimit, cancellationToken);
}
