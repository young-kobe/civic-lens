using CivicLens.Application;
using CivicLens.Application.Actions;
using CivicLens.Application.Analysis;
using CivicLens.Application.Collection.Jobs;
using CivicLens.Application.Collection.Processing;
using CivicLens.Application.Documents;
using CivicLens.Application.Issues;
using CivicLens.Host.Analysis;
using CivicLens.Host.Publication;
using CivicLens.Host.Statements;
using CivicLens.Infrastructure.Actions;
using CivicLens.Infrastructure.Analysis;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Collection.Jobs;
using CivicLens.Infrastructure.Documents;
using CivicLens.Infrastructure.Spending;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CivicLens.Host.Actions;

internal static class ActionDraftComposition
{
    public static AnalysisComposition<ActionDraftRun> Create(IDbContextFactory<CollectionAttemptDbContext> contexts, string connectionString,
        CodeVersion codeVersion, ICollectionJobStore jobs, ApiResponseReader responses, IDocumentTextExtractor textExtractor, IssueConfigurationRevision? issues,
        string artifactRoot, WorkerBeat beat, ILoggerFactory loggers)
    {
        var status = new PostgresAnalysisStatusStore(contexts, ActionDraftTask.Task);
        var configuration = ActionDraftConfiguration.FromEnvironment();
        if (configuration is null) return AnalysisComposition<ActionDraftRun>.Disabled(status);
        if (issues is null) throw new ArgumentException("Action drafting needs CIVIC_LENS_ISSUES_CONFIG.");
        var wakeup = PostgresAnalysisWakeup.FromConnectionString(connectionString, PostgresActionDraftStore.Table, ActionDraftTask.Task);
        var reader = new ActionBillReader(jobs, responses, textExtractor, artifactRoot);
        var step = new ActionDraftStep(new PostgresActionDraftStore(contexts, codeVersion), reader,
            new AnthropicActionDraftModel(configuration.ApiKey), configuration.Settings, issues);
        return new(status, configuration.Settings.DailyTokenLimit, new AnalysisWorker<ActionDraftRun>(step, status, TimeProvider.System, loggers.CreateLogger<AnalysisWorker<ActionDraftRun>>(), beat, wakeup), wakeup);
    }

    public static ActionTextMeasure CreateMeasure(CodeVersion codeVersion)
    {
        var (connectionString, _) = OwnerCommand.ReadEnvironment();
        var apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey)) throw new ArgumentException("The text measure needs ANTHROPIC_API_KEY.");
        var issues = StatementIssueRubric.FromEnvironment() ?? throw new ArgumentException("The text measure needs CIVIC_LENS_ISSUES_CONFIG.");
        var reader = new ActionBillReader(PostgresCollectionJobStore.FromConnectionString(connectionString, codeVersion),
            new ApiResponseReader(PostgresCollectionAttemptStore.FromConnectionString(connectionString), new CaptureBodyReader()),
            new CaptureDocumentTextExtractor(), PublicationComposition.RequireArtifactRoot());
        var store = new PostgresActionDraftStore(AnalysisDbContextFactory.Create(connectionString), codeVersion);
        var settings = new ActionDraftSettings(AnalysisSettings.DefaultCallTokenLimit, draftsFromBillText: true);
        return new(store, new ActionDraftStep(store, reader, new AnthropicActionDraftModel(apiKey), settings, issues),
            new PostgresAiSpendingStore(AnalysisDbContextFactory.Create(connectionString)));
    }
}
