using CivicLens.Application.Actions;
using CivicLens.Application.Analysis;
using CivicLens.Application.Collection.Jobs;
using CivicLens.Application.Collection.Processing;
using CivicLens.Application.Documents;
using CivicLens.Application.Issues;
using CivicLens.Host.Analysis;
using CivicLens.Infrastructure.Actions;
using CivicLens.Infrastructure.Analysis;
using CivicLens.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Host.Actions;

internal static class ActionDraftComposition
{
    public static AnalysisComposition<ActionDraftRun> Create(IDbContextFactory<CollectionAttemptDbContext> contexts, string connectionString,
        ICollectionJobStore jobs, ApiResponseReader responses, IDocumentTextExtractor textExtractor, IssueConfigurationRevision? issues,
        string artifactRoot)
    {
        var status = new PostgresAnalysisStatusStore(contexts, ActionDraftTask.Task);
        var configuration = ActionDraftConfiguration.FromEnvironment();
        if (configuration is null) return AnalysisComposition<ActionDraftRun>.Disabled(status);
        if (issues is null) throw new ArgumentException("Action drafting needs CIVIC_LENS_ISSUES_CONFIG.");
        var wakeup = PostgresAnalysisWakeup.FromConnectionString(connectionString, PostgresActionDraftStore.Table, ActionDraftTask.Task);
        var reader = new ActionBillReader(jobs, responses, textExtractor, artifactRoot);
        var step = new ActionDraftStep(new PostgresActionDraftStore(contexts), reader,
            new AnthropicActionDraftModel(configuration.ApiKey), configuration.Settings, issues);
        return new(status, configuration.Settings.DailyTokenLimit, new AnalysisWorker<ActionDraftRun>(step, status, TimeProvider.System, wakeup), wakeup);
    }
}
