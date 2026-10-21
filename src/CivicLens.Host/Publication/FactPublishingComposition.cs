using CivicLens.Application;
using CivicLens.Application.Collection;
using CivicLens.Application.Issues;
using CivicLens.Application.Publication;
using CivicLens.Host.Collection;
using CivicLens.Infrastructure;
using CivicLens.Infrastructure.Publication;

namespace CivicLens.Host.Publication;

internal sealed record FactPublishingComposition(AnalysisStage Stage, IWorkerWakeup Wakeup)
{
    private const string EnabledVariable = "CIVIC_LENS_FACT_PUBLISHING";

    public static FactPublishingComposition? Create(string connectionString, CollectionConfiguration collection, IssueConfiguration? issues)
    {
        var setting = Environment.GetEnvironmentVariable(EnabledVariable);
        if (string.IsNullOrWhiteSpace(setting)) return null;
        if (setting != "1") throw new ArgumentException($"{EnabledVariable} must be 1 or unset.");
        var releaseRoot = Environment.GetEnvironmentVariable("CIVIC_LENS_RELEASE_DIRECTORY");
        if (string.IsNullOrWhiteSpace(releaseRoot))
            throw new ArgumentException("Fact publishing needs CIVIC_LENS_RELEASE_DIRECTORY.");
        var publications = PostgresPublicationStore.FromConnectionString(connectionString);
        var (publish, loaders) = PublicationComposition.Create(connectionString, publications, new FileReleaseDirectory(releaseRoot),
            collection, PublicationComposition.RequireIssues(issues));
        var wakeup = PostgresIntervalWakeup.FromConnectionString(connectionString, "civic_lens_fact_publishing", TimeSpan.FromHours(1));
        Action<Exception> report = exception => Console.Error.WriteLine($"factPublishing failed: {exception.GetType().Name}: {exception.Message}");
        var worker = new StageWorker(new PublishCodeApprovedFacts(publish, publications, loaders, report, Console.Error.WriteLine).ExecuteAsync, wakeup, report);
        return new(new AnalysisStage("factPublishing", worker.ExecuteAsync), wakeup);
    }
}
