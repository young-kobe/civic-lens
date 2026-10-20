using CivicLens.Application;
using CivicLens.Application.Collection;
using CivicLens.Application.Issues;
using CivicLens.Application.Publication;
using CivicLens.Host.Collection;
using CivicLens.Infrastructure.Publication;

namespace CivicLens.Host.Publication;

internal sealed record FactPublishingComposition(AnalysisStage Stage, IWorkerWakeup Wakeup)
{
    private const string EnabledVariable = "CIVIC_LENS_FACT_PUBLISHING";

    public static FactPublishingComposition? Create(string connectionString, CollectionConfiguration collection, IssueConfiguration? issues)
    {
        var setting = Environment.GetEnvironmentVariable(EnabledVariable);
        if (string.IsNullOrEmpty(setting)) return null;
        if (setting != "1") throw new ArgumentException($"{EnabledVariable} must be 1 or unset.");
        var releaseRoot = Environment.GetEnvironmentVariable("CIVIC_LENS_RELEASE_DIRECTORY");
        if (string.IsNullOrWhiteSpace(releaseRoot))
            throw new ArgumentException("Fact publishing needs CIVIC_LENS_RELEASE_DIRECTORY.");
        if (issues is null) throw new ArgumentException("Fact publishing needs CIVIC_LENS_ISSUES_CONFIG.");
        var (publish, loaders) = PublicationComposition.Create(connectionString,
            PostgresPublicationStore.FromConnectionString(connectionString), new FileReleaseDirectory(releaseRoot), collection, issues);
        var wakeup = PostgresFactPublishingWakeup.FromConnectionString(connectionString);
        var worker = new FactPublishingWorker(new PublishCodeApprovedFacts(publish, loaders), wakeup,
            exception => Console.Error.WriteLine($"factPublishing failed: {WorkerLoop.ErrorCode(exception)}"));
        return new(new AnalysisStage("factPublishing", worker.ExecuteAsync), wakeup);
    }
}
