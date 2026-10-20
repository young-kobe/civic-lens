using CivicLens.Application.Collection;
using CivicLens.Application.Issues;
using CivicLens.Application.Publication;
using CivicLens.Host.Review;
using CivicLens.Infrastructure.Analysis;
using CivicLens.Infrastructure.Documents;
using CivicLens.Infrastructure.Publication;
using CivicLens.Infrastructure.Relationships;
using CivicLens.Infrastructure.Briefs;
using CivicLens.Infrastructure.Actions;
using CivicLens.Infrastructure.Review;
using CivicLens.Infrastructure.Roster;
using CivicLens.Infrastructure.Statements;
using CivicLens.Infrastructure.Votes;

namespace CivicLens.Host.Publication;

internal static class PublicationComposition
{
    public static (PublishRelease Publish, PublishedRecordLoaders Loaders) Create(string connectionString,
        PostgresPublicationStore publications, FileReleaseDirectory releases, CollectionConfiguration collection, IssueConfiguration issues)
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
                PostgresExecutiveRosterStore.FromConnectionString(connectionString)), TimeProvider.System);
        return (publish, loaders);
    }
}
