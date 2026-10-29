using CivicLens.Application;
using CivicLens.Application.Analysis;
using CivicLens.Application.Collection.Jobs;
using CivicLens.Application.Collection.Processing;
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
        ICollectionJobStore jobs, ApiResponseReader responses, IssueConfigurationRevision? issues, string artifactRoot, WorkerBeat beat, ILoggerFactory loggers)
    {
        var status = new PostgresAnalysisStatusStore(contexts, VoteDraftTask.Task);
        var configuration = VoteDraftConfiguration.FromEnvironment();
        if (configuration is null) return AnalysisComposition<VoteDraftRun>.Disabled(status);
        if (issues is null) throw new ArgumentException("Vote drafting needs CIVIC_LENS_ISSUES_CONFIG.");
        var wakeup = PostgresAnalysisWakeup.FromConnectionString(connectionString, PostgresVoteDraftStore.Table, VoteDraftTask.Task);
        var step = new VoteDraftStep(new PostgresVoteDraftStore(contexts), new VoteReader(jobs, new PostgresMemberRosterStore(contexts), responses, artifactRoot),
            new AnthropicVoteDraftModel(configuration.ApiKey), configuration.Settings, issues);
        return new(status, configuration.Settings.DailyTokenLimit, new AnalysisWorker<VoteDraftRun>(step, status, TimeProvider.System, loggers.CreateLogger<AnalysisWorker<VoteDraftRun>>(), wakeup, beat: beat), wakeup);
    }
}
