using CivicLens.Tests.Fixtures;
using CivicLens.Application.Issues;
using CivicLens.Infrastructure.Relationships;
using CivicLens.Infrastructure.Votes;
using System.Security.Cryptography;
using System.Text.Json;
using CivicLens.Application.Publication;
using CivicLens.Core.Review;
using CivicLens.Host.Publication;
using CivicLens.Infrastructure.Analysis;
using CivicLens.Infrastructure.Briefs;
using CivicLens.Infrastructure.Publication;
using CivicLens.Infrastructure.Review;
using CivicLens.Infrastructure.Statements;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Application.Publication;

namespace CivicLens.Tests.Infrastructure.Collection.BillStream;

public sealed partial class PostgresBillStreamTests
{
    private static readonly DateTimeOffset ReleasedAt = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AnActionDecisionAfterTheBuildBlocksTheCommitBecauseApprovalMayHaveChanged()
    {
        await DraftForReviewAsync(UpdatedAt, DraftableOutput);
        await DecideAsync(1, 0, ReviewDecisionKind.Approve);
        var publications = PostgresPublicationStore.FromConnectionString(connectionString);

        await Assert.ThrowsAsync<PublicationConflictException>(() =>
            publications.CommitAsync(PublicationScenario.Owner.Subject, ActionCommit(reviewStateVersion: 0), "stale", ReleaseHash(1), default));
        Assert.Equal(new PublicationState(0, null), await publications.GetStateAsync(default));
        var committed = await publications.CommitAsync(PublicationScenario.Owner.Subject, ActionCommit(reviewStateVersion: 1), "fresh",
            ReleaseHash(2), default);
        Assert.Equal(1, committed.ReleaseNumber);
    }

    [Fact]
    public async Task ACommittedActionLeavesTheReadyListBecauseTheReleaseRowCarriesTheActionKind()
    {
        await DraftAsync(UpdatedAt, DraftableOutput);
        var ready = Assert.Single(await ActionReviews().ListUnpublishedApprovedAsync(10, default));
        Assert.Null(ready.PublishedRevisionNumber);

        await PostgresPublicationStore.FromConnectionString(connectionString).CommitAsync(PublicationScenario.Owner.Subject,
            ActionCommit(reviewStateVersion: 1), "key", ReleaseHash(1), default);

        Assert.Equal("action", await ScalarAsync("SELECT records_json::jsonb -> 0 ->> 'kind' FROM publication_releases"));
        Assert.Empty(await ActionReviews().ListUnpublishedApprovedAsync(10, default));
    }

    [Fact]
    public async Task PublicationReadReturnsTheApprovedActionWithEachCitedExtractionAndOmitsUnknownIds()
    {
        await DraftForReviewAsync(UpdatedAt, DraftableOutput);
        await DecideAsync(1, 0, ReviewDecisionKind.Approve);

        var found = await ActionReviews().GetForPublicationAsync([RecordId, new string('f', 64)], default);

        var read = Assert.Single(found);
        Assert.Equal(RecordId, read.Key);
        Assert.Equal(ReviewStatus.Approved, read.Value.Review.CurrentStatus);
        Assert.Equal(read.Value.Review.CurrentRevision.Citations.Select(span => span.ExtractionId).Distinct().Order(),
            read.Value.Extractions.Keys.Order());
    }

    // The saved Congress.gov pages pass the contract check, so a drafted citation never blocks a publish.
    [Fact]
    public async Task AnApprovedDraftedActionPublishesWithEveryCitationQuotingItsSavedApiPage()
    {
        await DraftForReviewAsync(UpdatedAt, DraftableOutput);
        await DecideAsync(1, 0, ReviewDecisionKind.Approve);
        var revision = (await ActionReviews().GetRevisionAsync(RecordId, 1, default))!;
        var names = revision.Sponsorships.Select(item => item.PersonId).Distinct().ToDictionary(id => id, id => $"Official {id}");
        var releases = new FileReleaseDirectory(Path.Combine(captureRoot, "releases"));
        var publisher = new PublishRelease(PostgresPublicationStore.FromConnectionString(connectionString), releases, new ReleaseRenderer(),
            new PublishedRecordLoaders(PostgresDocumentChangeReviewStore.FromConnectionString(connectionString),
                PostgresStatementReviewStore.FromConnectionString(connectionString), ActionReviews(),
                PostgresVoteReviewStore.FromConnectionString(connectionString),
                new PostgresRelationshipReviewStore(AnalysisDbContextFactory.Create(connectionString)), new PostgresBriefReviewStore(AnalysisDbContextFactory.Create(connectionString))),
            new PublicationCatalog(names, IssueConfiguration.None), FixedRosters.NoOfficials(), TimeProvider.System);

        var summary = await publisher.ExecuteAsync(PublicationScenario.Owner,
            PublicationScenario.Request("publish", new PublishedRecordReference(PublishedRecordKind.Action, RecordId)));

        var record = JsonSerializer.Deserialize<PublishedAction>(await releases.ReadFileAsync(summary.DirectoryName,
            PublicationProtocol.RecordDataPath(RecordId), PublicationProtocol.MaximumManifestBytes, default), PublicationProtocol.JsonOptions)!;
        record.Validate();
        Assert.Equal(revision.Citations.Select(span => span.ExtractionId).Distinct(), record.Sources.Select(source => source.ExtractionId));
        Assert.Equal(revision.Sponsorships.Length, record.Sponsorships.Length);
    }

    private static PublicationCommit ActionCommit(int reviewStateVersion) =>
        new(1, null, $"000001-{Guid.NewGuid():N}", [new(PublishedRecordKind.Action, RecordId, 1)],
            [new(PublishedRecordKind.Action, RecordId, 1, reviewStateVersion)]);

    private static string ReleaseHash(int seed) => Convert.ToHexStringLower(SHA256.HashData(BitConverter.GetBytes(seed)));
}
