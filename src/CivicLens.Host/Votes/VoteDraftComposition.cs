using CivicLens.Application;
using CivicLens.Application.Analysis;
using CivicLens.Application.Collection.Jobs;
using CivicLens.Application.Collection.Processing;
using CivicLens.Application.Documents;
using CivicLens.Host.Publication;
using CivicLens.Host.Statements;
using CivicLens.Infrastructure.Collection.Jobs;
using CivicLens.Infrastructure.Documents;
using CivicLens.Application.Issues;
using CivicLens.Application.Votes;
using CivicLens.Host.Analysis;
using CivicLens.Infrastructure.Analysis;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Roster;
using CivicLens.Infrastructure.Votes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CivicLens.Host.Votes;

internal static class VoteDraftComposition
{
    public static AnalysisComposition<VoteDraftRun> Create(IDbContextFactory<CollectionAttemptDbContext> contexts, string connectionString,
        ICollectionJobStore jobs, ApiResponseReader responses, IDocumentTextExtractor textExtractor, IssueConfigurationRevision? issues, string artifactRoot, WorkerBeat beat, ILoggerFactory loggers)
    {
        var status = new PostgresAnalysisStatusStore(contexts, VoteDraftTask.Task);
        var configuration = VoteDraftConfiguration.FromEnvironment();
        if (configuration is null) return AnalysisComposition<VoteDraftRun>.Disabled(status);
        if (issues is null) throw new ArgumentException("Vote drafting needs CIVIC_LENS_ISSUES_CONFIG.");
        var wakeup = PostgresAnalysisWakeup.FromConnectionString(connectionString, PostgresVoteDraftStore.Table, VoteDraftTask.Task);
        var step = new VoteDraftStep(new PostgresVoteDraftStore(contexts), new VoteReader(jobs, responses, textExtractor, artifactRoot),
            new AnthropicVoteDraftModel(configuration.ApiKey), configuration.Settings, issues);
        return new(status, configuration.Settings.DailyTokenLimit, new AnalysisWorker<VoteDraftRun>(step, status, TimeProvider.System, loggers.CreateLogger<AnalysisWorker<VoteDraftRun>>(), beat, wakeup), wakeup);
    }
    public static VoteDecidesMeasure CreateMeasure(CodeVersion codeVersion)
    {
        var (connectionString, _) = OwnerCommand.ReadEnvironment();
        var apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey)) throw new ArgumentException("The vote measure needs ANTHROPIC_API_KEY.");
        var issues = StatementIssueRubric.FromEnvironment() ?? throw new ArgumentException("The vote measure needs CIVIC_LENS_ISSUES_CONFIG.");
        var reader = new VoteReader(PostgresCollectionJobStore.FromConnectionString(connectionString, codeVersion),
            new ApiResponseReader(PostgresCollectionAttemptStore.FromConnectionString(connectionString), new CaptureBodyReader()),
            new CaptureDocumentTextExtractor(), PublicationComposition.RequireArtifactRoot());
        var store = new PostgresVoteDraftStore(AnalysisDbContextFactory.Create(connectionString));
        return new(store, new VoteDraftStep(store, reader, new AnthropicVoteDraftModel(apiKey), new VoteDraftSettings(AnalysisSettings.DefaultCallTokenLimit, concurrency: 1), issues));
    }
}
