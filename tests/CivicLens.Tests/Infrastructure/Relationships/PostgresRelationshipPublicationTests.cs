using System.Security.Cryptography;
using System.Text;
using CivicLens.Application.Publication;
using CivicLens.Application.Relationships;
using CivicLens.Core.Relationships;
using CivicLens.Core.Review;
using CivicLens.Infrastructure.Analysis;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Publication;
using CivicLens.Infrastructure.Relationships;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Application.Relationships;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using static CivicLens.Tests.Fixtures.RelationshipFixtures;

namespace CivicLens.Tests.Infrastructure.Relationships;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PostgresRelationshipPublicationTests(PostgresCollection postgres) : IAsyncLifetime
{
    private static readonly ReviewActor Reviewer = new("auth0|reviewer", ReviewRole.Reviewer);
    private static readonly DateTimeOffset ReleasedAt = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);
    private static readonly PublishedRevisionBinding StatementBinding = BriefWorldSeeder.Binding(PublishedRecordKind.Statement, StatementId, 1);
    private static readonly PublishedRevisionBinding ActionBinding = BriefWorldSeeder.Binding(PublishedRecordKind.Action, ActionId, 1);

    private string connectionString = null!;
    private PostgresRelationshipReviewStore reviews = null!;
    private PostgresPublicationStore publications = null!;
    private RelationshipWorldSeeder seeder = null!;
    private BriefWorldSeeder releases = null!;
    private string recordId = null!;

    public async Task InitializeAsync()
    {
        connectionString = await postgres.CreateMigratedDatabaseAsync();
        var factory = new PooledDbContextFactory<CollectionAttemptDbContext>(
            new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options);
        var drafts = new PostgresRelationshipDraftStore(factory);
        reviews = new PostgresRelationshipReviewStore(factory);
        publications = new PostgresPublicationStore(factory);
        seeder = new RelationshipWorldSeeder(connectionString);
        releases = new BriefWorldSeeder(connectionString);
        await seeder.StatementAsync(Statement());
        await seeder.ActionAsync(Action());
        recordId = ActionPair().PairId;
        _ = await drafts.RefreshPairsAsync(default);
        await AnalysisWorkers.RelationshipDrafting(drafts, new PostgresAnalysisStatusStore(factory, RelationshipDraftTask.Task),
            new FakeRelationshipDraftModel().Draft(Output(quote: BillWords)), new RelationshipDraftSettings(10_000_000, concurrency: 1), Officials,
            TimeProvider.System).ExecuteAsync(once: true);
        await releases.ReleaseAsync(1, ReleasedAt, active: true, StatementBinding, ActionBinding);
    }

    public Task DisposeAsync() => postgres.DropDatabaseAsync(connectionString);

    [Fact]
    public async Task TheReadyListHoldsAnApprovedRelationshipUntilACitedRecordLosesItsApprovalBecausePublishWouldRefuseIt()
    {
        Assert.Empty(await reviews.ListUnpublishedApprovedAsync(10, [], default));
        await ApproveAsync();

        var ready = Assert.Single(await reviews.ListUnpublishedApprovedAsync(10, [], default));
        Assert.Equal((recordId, false), (ready.Relationship.RecordId, ready.CitedRecordsChanged));

        await releases.DecisionAsync(PublishedRecordKind.Statement, StatementId, 1, 2, ReviewDecisionKind.WithdrawApproval);

        Assert.True(Assert.Single(await reviews.ListUnpublishedApprovedAsync(10, [], default)).CitedRecordsChanged);
    }

    [Fact]
    public async Task TheReadyListLeavesOutARelationshipThatTheActiveReleaseHoldsAtItsCurrentRevision()
    {
        await ApproveAsync();
        await releases.ReplaceActiveReleaseAsync(2, ReleasedAt.AddHours(1), StatementBinding, ActionBinding,
            BriefWorldSeeder.Binding(PublishedRecordKind.Relationship, recordId, 1));

        Assert.Empty(await reviews.ListUnpublishedApprovedAsync(10, [], default));
    }

    [Fact]
    public async Task TheCitedRecordsAreCurrentOnlyWhenTheGivenReleaseHoldsTheirExactRevisionsAndTheirLatestDecisionIsAnApproval()
    {
        await ApproveAsync();

        Assert.False(await ChangedAsync(StatementBinding, ActionBinding));
        Assert.True(await ChangedAsync(ActionBinding));
        Assert.True(await ChangedAsync(BriefWorldSeeder.Binding(PublishedRecordKind.Statement, StatementId, 2), ActionBinding));
        await releases.DecisionAsync(PublishedRecordKind.Action, ActionId, 1, 2, ReviewDecisionKind.RequestChanges);
        Assert.True(await ChangedAsync(StatementBinding, ActionBinding));
    }

    [Fact]
    public async Task TheReadReturnsTheCitedRevisionsAndNotTheNewerOnesBecauseThePublishedLinkNamesTheRevisionThatAPersonApproved()
    {
        await ApproveAsync();
        await seeder.ActionAsync(Action() with { TrackedIssueIds = ["housing"], PolicyAreaId = "housing-development" });
        await seeder.StatementAsync(Statement(revision: 2) with { Revision = Statement(revision: 2).Revision with { Headline = "Warren changed" } });
        await seeder.ActionAsync(Action(revision: 2) with { Headline = "A newer action headline" });

        var read = (await reviews.GetForPublicationAsync([recordId], [StatementBinding, ActionBinding], default))[recordId];

        Assert.Equal((1, "Warren on rent", 1, ActionRevisionFixtures.Model().Headline),
            (read.Statement.Revision.RevisionNumber, read.Statement.Revision.Headline, read.Target.RevisionNumber, read.Target.Headline));
        Assert.Equal(Article, read.Statement.ArticleText);
        Assert.Equal(["housing"], [.. read.Target.IssueIds]);
        Assert.Equal(["housing-development"], [.. read.Target.PolicyAreaIds]);
    }

    [Fact]
    public async Task TheReadFailsLoudWhenACitedRevisionIsNotStoredInsteadOfDroppingTheSide()
    {
        await releases.ExecuteAsync("SET session_replication_role = replica; DELETE FROM statement_revisions WHERE candidate_id = @id", ("id", StatementId));

        await Assert.ThrowsAsync<InvalidDataException>(() => reviews.GetAsync(recordId, default));
    }

    [Fact]
    public async Task AnIdWithoutADraftIsLeftOutOfThePublicationRead()
    {
        await ApproveAsync();

        var found = await reviews.GetForPublicationAsync([recordId, new string('f', 64)], [StatementBinding, ActionBinding], default);

        Assert.Equal([recordId], found.Keys);
    }

    [Fact]
    public async Task TheCommitRefusesARelationshipWhoseCitedRecordIsMissingFromTheNewReleaseSoThePageNeverLinksToNothing()
    {
        await ApproveAsync();

        await Assert.ThrowsAsync<PublicationConflictException>(() => CommitAsync(StatementBinding, RelationshipBinding()));

        Assert.Single(await publications.ListAsync(10, default));
    }

    [Fact]
    public async Task TheCommitRefusesARelationshipWhoseCitedRecordTheSamePublishReplacesWithANewerRevision()
    {
        await ApproveAsync();
        await seeder.StatementAsync(Statement(revision: 2));
        await releases.DecisionAsync(PublishedRecordKind.Statement, StatementId, 2, 2, ReviewDecisionKind.Approve);

        await Assert.ThrowsAsync<PublicationConflictException>(() =>
            CommitAsync(BriefWorldSeeder.Binding(PublishedRecordKind.Statement, StatementId, 2), ActionBinding, RelationshipBinding()));

        Assert.Single(await publications.ListAsync(10, default));
    }

    [Fact]
    public async Task TheCommitRefusesARelationshipWhenACitedRecordWasWithdrawnBetweenTheBuildAndTheCommit()
    {
        await ApproveAsync();
        await releases.DecisionAsync(PublishedRecordKind.Statement, StatementId, 1, 2, ReviewDecisionKind.WithdrawApproval);

        await Assert.ThrowsAsync<PublicationConflictException>(() => CommitAsync(StatementBinding, ActionBinding, RelationshipBinding()));

        Assert.Single(await publications.ListAsync(10, default));
    }

    [Fact]
    public async Task TheCommitAcceptsARelationshipWhoseCitedRecordsAreCurrentInTheNewRelease()
    {
        await ApproveAsync();

        var summary = await CommitAsync(StatementBinding, ActionBinding, RelationshipBinding());

        Assert.Equal(2, summary.ReleaseNumber);
    }

    [Fact]
    public async Task TheCommitChecksTheRelationshipTableForARelationshipAndRefusesAChangedReviewState()
    {
        await ApproveAsync();
        await seeder.ExecuteAsync($"UPDATE relationship_drafts SET review_state_version = review_state_version + 1 WHERE record_id = '{recordId}'");

        await Assert.ThrowsAsync<PublicationConflictException>(() => CommitAsync(StatementBinding, ActionBinding, RelationshipBinding()));
    }

    [Fact]
    public async Task FindCitingNamesOnlyTheGivenRelationshipsThatCiteAGivenRecordOfTheRightKind()
    {
        var statement = new PublishedRecordReference(PublishedRecordKind.Statement, StatementId);
        var action = new PublishedRecordReference(PublishedRecordKind.Action, ActionId);

        Assert.Equal([recordId], await reviews.FindCitingAsync([recordId], [statement], default));
        Assert.Equal([recordId], await reviews.FindCitingAsync([recordId], [action], default));
        Assert.Empty(await reviews.FindCitingAsync([recordId], [new(PublishedRecordKind.Vote, ActionId), new(PublishedRecordKind.Statement, ActionId)], default));
        Assert.Empty(await reviews.FindCitingAsync([recordId], [new(PublishedRecordKind.Statement, new string('f', 64))], default));
        await Assert.ThrowsAsync<InvalidDataException>(() => reviews.FindCitingAsync([recordId, new string('f', 64)], [statement, action], default));
        Assert.Empty(await reviews.FindCitingAsync([], [statement], default));
    }

    private PublishedRevisionBinding RelationshipBinding() => BriefWorldSeeder.Binding(PublishedRecordKind.Relationship, recordId, 1);

    private async Task ApproveAsync() => _ = await new DecideRelationshipReview(reviews).ExecuteAsync(Reviewer,
        new(recordId, 1, 0, ReviewDecisionKind.Approve, null, [], "approve-1"));

    private async Task<bool> ChangedAsync(params PublishedRevisionBinding[] release) =>
        (await reviews.GetForPublicationAsync([recordId], [.. release], default))[recordId].CitedRecordsChanged;

    private Task<PublicationReleaseSummary> CommitAsync(params PublishedRevisionBinding[] records) =>
        publications.CommitAsync("auth0|owner", new(2, 1, $"000002-{Guid.NewGuid():N}", [.. records],
            [new(PublishedRecordKind.Relationship, recordId, 1, 1)]), "commit-1",
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("payload"))), default);
}
