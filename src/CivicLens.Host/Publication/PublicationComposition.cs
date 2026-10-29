using CivicLens.Application.Collection;
using CivicLens.Application.Issues;
using CivicLens.Application.Publication;
using CivicLens.Host.Review;
using CivicLens.Infrastructure.Analysis;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Documents;
using CivicLens.Infrastructure.Publication;
using CivicLens.Infrastructure.Relationships;
using CivicLens.Infrastructure.Briefs;
using CivicLens.Infrastructure.Actions;
using CivicLens.Infrastructure.Review;
using CivicLens.Infrastructure.Roster;
using CivicLens.Infrastructure.Statements;
using CivicLens.Infrastructure.Votes;
using Microsoft.Extensions.Logging;

namespace CivicLens.Host.Publication;

internal sealed record PublicationParts(PublishRelease Publish, PublishedRecordLoaders Loaders);

internal static class PublicationComposition
{
    private const string ArtifactDirectoryVariable = "CIVIC_LENS_ARTIFACT_DIRECTORY";

    public static string RequireArtifactRoot() =>
        Environment.GetEnvironmentVariable(ArtifactDirectoryVariable) is { Length: > 0 } root
            ? Path.GetFullPath(root)
            : throw new ArgumentException($"Publishing requires {ArtifactDirectoryVariable}.");

    public static IssueConfiguration RequireIssues(IssueConfiguration? issues) =>
        issues ?? throw new ArgumentException("Publishing requires CIVIC_LENS_ISSUES_CONFIG.");

    public static PublicationParts Create(string connectionString,
        PostgresPublicationStore publications, FileReleaseDirectory releases, CollectionConfiguration collection, IssueConfiguration issues,
        string artifactRoot, ILoggerFactory loggers)
    {
        var loaders = new PublishedRecordLoaders(
            PostgresDocumentChangeReviewStore.FromConnectionString(connectionString),
            PostgresStatementReviewStore.FromConnectionString(connectionString),
            PostgresActionReviewStore.FromConnectionString(connectionString),
            PostgresVoteReviewStore.FromConnectionString(connectionString),
            new PostgresRelationshipReviewStore(AnalysisDbContextFactory.Create(connectionString)),
            new PostgresBriefReviewStore(AnalysisDbContextFactory.Create(connectionString)));
        var publish = new PublishRelease(publications, releases, ReleaseRenderer.FromEnvironment(), loaders,
            new PublicationCatalog(ReviewOfficials.From(collection).Names, issues),
            new PublicationOfficials(collection, PostgresMemberRosterStore.FromConnectionString(connectionString),
                PostgresExecutiveRosterStore.FromConnectionString(connectionString)),
            new ReleasePhotos(PostgresCollectionAttemptStore.FromConnectionString(connectionString), new CaptureBodyReader(), artifactRoot,
                loggers.CreateLogger<ReleasePhotos>()),
            TimeProvider.System, new PruneReleases(publications, releases, loggers.CreateLogger<PruneReleases>()));
        return new(publish, loaders);
    }
}
