using CivicLens.Application;
using CivicLens.Application.Collection;
using CivicLens.Application.Issues;
using CivicLens.Application.Publication;
using CivicLens.Host.Collection;
using CivicLens.Infrastructure;
using CivicLens.Infrastructure.Publication;
using Microsoft.Extensions.Logging;

namespace CivicLens.Host.Publication;

internal sealed record PublishingComposition(AnalysisStage Stage, IWorkerWakeup Wakeup)
{
    private const string EnabledVariable = "CIVIC_LENS_PUBLISHING";

    public static PublishingComposition? Create(string connectionString, CollectionConfiguration collection, IssueConfiguration? issues,
        string artifactRoot, WorkerBeat beat, ILoggerFactory loggers)
    {
        var setting = Environment.GetEnvironmentVariable(EnabledVariable);
        if (string.IsNullOrWhiteSpace(setting)) return null;
        if (setting != "1") throw new ArgumentException($"{EnabledVariable} must be 1 or unset.");
        var releaseRoot = Environment.GetEnvironmentVariable("CIVIC_LENS_RELEASE_DIRECTORY");
        if (string.IsNullOrWhiteSpace(releaseRoot))
            throw new ArgumentException("Publishing needs CIVIC_LENS_RELEASE_DIRECTORY.");
        var publications = PostgresPublicationStore.FromConnectionString(connectionString);
        var (publish, loaders) = PublicationComposition.Create(connectionString, publications, new FileReleaseDirectory(releaseRoot),
            collection, PublicationComposition.RequireIssues(issues), artifactRoot, loggers);
        var wakeup = PostgresIntervalWakeup.FromConnectionString(connectionString, "civic_lens_publishing", TimeSpan.FromHours(1));
        var publishApproved = new PublishApprovedRecords(publish, publications, loaders, loggers.CreateLogger<PublishApprovedRecords>(), beat);
        var worker = new StageWorker(WorkerLoopNames.Publishing, publishApproved.ExecuteAsync, wakeup, loggers.CreateLogger<StageWorker>(), beat);
        return new(new AnalysisStage(WorkerLoopNames.Publishing, worker.ExecuteAsync), wakeup);
    }
}
