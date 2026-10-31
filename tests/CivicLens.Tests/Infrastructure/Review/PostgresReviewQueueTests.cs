using CivicLens.Application.Paging;
using CivicLens.Application.Review;
using CivicLens.Core.Relationships;
using CivicLens.Core.Review;
using CivicLens.Infrastructure.Analysis;
using CivicLens.Infrastructure.Review;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Infrastructure.Collection;

namespace CivicLens.Tests.Infrastructure.Review;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PostgresReviewQueueTests(PostgresCollection postgres) : IAsyncLifetime
{
    private static readonly string ActionId = RelationshipFixtures.ActionId;
    private static readonly string VoteId = RelationshipFixtures.VoteId;
    private static readonly string StatementId = RelationshipFixtures.StatementId;
    private readonly string captureRoot = Path.Combine(Path.GetTempPath(), "review-queue-" + Guid.NewGuid().ToString("N"));
    private string connectionString = null!;
    private PostgresReviewQueueStore store = null!;
    private RelationshipWorldSeeder seeder = null!;

    public async Task InitializeAsync()
    {
        connectionString = await postgres.CreateMigratedDatabaseAsync();
        store = new PostgresReviewQueueStore(AnalysisDbContextFactory.Create(connectionString));
        seeder = new RelationshipWorldSeeder(connectionString);
    }

    public async Task DisposeAsync()
    {
        await postgres.DropDatabaseAsync(connectionString);
        if (Directory.Exists(captureRoot)) Directory.Delete(captureRoot, recursive: true);
    }

    [Fact]
    public async Task TheQueueMergesKindsNewestRevisionFirstAndLeavesOutApprovedRecordsAndDraftsWithoutARevision()
    {
        await SeedThreeKindsAsync();
        await seeder.ExecuteAsync("""
            SET session_replication_role = replica;
            INSERT INTO statement_drafts (candidate_id, current_revision_number, review_state_version, review_status, created_at_utc_ticks, last_revised_at_utc_ticks)
            VALUES (repeat('2', 64), 0, 0, 'NotDrafted', 0, 999)
            """);
        var approved = RelationshipFixtures.Action(5) with { RecordId = new('d', 64), CandidateId = new('d', 64) };
        await seeder.ActionAsync(approved, "Approved", revisedAtTicks: 900);

        var page = await store.ListAsync(null, 10, default);

        Assert.Equal([StatementId, VoteId, ActionId], page.Items.Select(item => item.RecordId));
        Assert.Equal([PublishedRecordKind.Statement, PublishedRecordKind.Vote, PublishedRecordKind.Action], page.Items.Select(item => item.Kind));
        Assert.Equal(3, page.Total);
        Assert.Equal("Warren on rent", page.Items[0].Headline);
        Assert.All(page.Items, item => Assert.Equal(ReviewStatus.AwaitingReview, item.Status));
        Assert.Equal(new DateTimeOffset(300, TimeSpan.Zero), page.Items[0].LastRevisedAtUtc);
    }

    [Fact]
    public async Task AHeldReasonReachesTheQueueItemBecauseItExplainsWhyCodeLeftTheRecordForAPerson()
    {
        await SeedThreeKindsAsync();
        await seeder.ExecuteAsync("UPDATE action_drafts SET held_reason = 'The sponsor is not on the bill.' WHERE record_id = @id", ("id", ActionId));

        var page = await store.ListAsync(null, 10, default);

        Assert.Equal("The sponsor is not on the bill.", page.Items.Single(item => item.RecordId == ActionId).Reason);
        Assert.Equal("A person edited this record. A person must approve it.", page.Items.Single(item => item.RecordId == VoteId).Reason);
    }

    [Fact]
    public async Task PagesWalkEveryRecordOnceEvenWhenTwoKindsShareARevisionTimeBecauseTheKindCodeBreaksTheTie()
    {
        await SeedThreeKindsAsync();

        var first = await store.ListAsync(null, 1, default);
        var second = await store.ListAsync(PageCursor.Parse(first.OlderCursor), 1, default);
        var third = await store.ListAsync(PageCursor.Parse(second.OlderCursor), 1, default);
        var back = await store.ListAsync(PageCursor.Parse(third.NewerCursor), 1, default);

        Assert.Equal([StatementId, VoteId, ActionId], new[] { first, second, third }.Select(page => page.Items.Single().RecordId));
        Assert.Null(third.OlderCursor);
        Assert.Equal(VoteId, back.Items.Single().RecordId);
    }

    [Fact]
    public async Task EveryKindAppearsWithItsOwnRecordIdAndHeadlineAndARelationshipNamesItsCitedStatement()
    {
        var recordIds = await SeedEveryKindAsync();

        var page = await store.ListAsync(null, 10, default);

        Assert.Equal(7, page.Total);
        Assert.Equal(Enum.GetValues<PublishedRecordKind>().Order(), page.Items.Select(item => item.Kind).Order());
        foreach (var item in page.Items) Assert.Equal(recordIds[item.Kind], item.RecordId);
        Assert.Equal("Fee rose", Of(page, PublishedRecordKind.DocumentChange).Headline);
        Assert.Equal("Warren on rent", Of(page, PublishedRecordKind.Statement).Headline);
        Assert.Equal("Warren on rent", Of(page, PublishedRecordKind.Relationship).Headline);
        Assert.Equal(RelationshipType.DescribesOwnBill, Of(page, PublishedRecordKind.Relationship).LinkType);
        Assert.Equal("A week of housing news", Of(page, PublishedRecordKind.Brief).Headline);
        Assert.Equal(RelationshipFixtures.Action().Headline, Of(page, PublishedRecordKind.Action).Headline);
        Assert.Equal(RelationshipFixtures.Vote().Headline, Of(page, PublishedRecordKind.Vote).Headline);
        var executive = Of(page, PublishedRecordKind.ExecutiveAction);
        Assert.Equal(FederalRegisterFixtures.Document(FederalRegisterFixtures.TrumpOrder).Title, executive.Headline);
        Assert.Equal("An executive action fact must equal the text of its citation.", executive.HeldReason);
    }

    private static ReviewQueueItem Of(ReviewQueuePage page, PublishedRecordKind kind) => page.Items.Single(item => item.Kind == kind);

    [Fact]
    public async Task AnExecutiveActionWithNoHeldReasonIsNotCalledAPersonEditBecauseCodeWritesEveryRevisionOfThatKind()
    {
        var executiveId = await new ExecutiveActionSeeder(connectionString, captureRoot)
            .AdmitAsync(FederalRegisterFixtures.TrumpOrder, "donald-trump", shiftedStoredTextPrefix: "Shifted. ");
        await seeder.ExecuteAsync("UPDATE executive_action_drafts SET held_reason = NULL WHERE record_id = @id", ("id", executiveId));

        var page = await store.ListAsync(null, 10, default);

        Assert.Equal("Code did not approve this record. A person must decide.", Assert.Single(page.Items).Reason);
    }

    private async Task<Dictionary<PublishedRecordKind, string>> SeedEveryKindAsync()
    {
        var statement = RelationshipFixtures.Statement(dated: false);
        await seeder.StatementAsync(statement, "AwaitingReview");
        var action = RelationshipFixtures.Action();
        await seeder.ActionAsync(action, "AwaitingReview", revisedAtTicks: 200);
        var vote = RelationshipFixtures.Vote();
        await seeder.VoteAsync(vote, "AwaitingReview", revisedAtTicks: 200);
        var relationship = RelationshipFixtures.Revision();
        await seeder.RevisionAsync(relationship);
        var documentChangeId = new string('d', 32);
        var briefs = new BriefWorldSeeder(connectionString);
        await seeder.ExecuteAsync("""
            SET session_replication_role = replica;
            INSERT INTO document_change_drafts (draft_id, comparison_id, current_revision_number, review_state_version, review_status, created_at_utc_ticks, last_revised_at_utc_ticks)
            VALUES (@id, repeat('c', 64), 1, 0, 'AwaitingReview', 0, 100)
            """, ("id", documentChangeId));
        await briefs.DocumentChangeAsync(documentChangeId, 1, "Fee rose");
        var brief = BriefFixtures.ReviewableRevision();
        await briefs.ReviewableBriefAsync(brief);
        var executiveId = await new ExecutiveActionSeeder(connectionString, captureRoot)
            .AdmitAsync(FederalRegisterFixtures.TrumpOrder, "donald-trump", shiftedStoredTextPrefix: "Shifted. ");
        return new()
        {
            [PublishedRecordKind.DocumentChange] = documentChangeId,
            [PublishedRecordKind.Statement] = statement.RecordId,
            [PublishedRecordKind.Action] = action.RecordId,
            [PublishedRecordKind.Vote] = vote.RecordId,
            [PublishedRecordKind.Brief] = brief.RecordId,
            [PublishedRecordKind.Relationship] = relationship.RecordId,
            [PublishedRecordKind.ExecutiveAction] = executiveId
        };
    }

    private async Task SeedThreeKindsAsync()
    {
        await seeder.StatementAsync(RelationshipFixtures.Statement(dated: false), "AwaitingReview");
        await seeder.ExecuteAsync("UPDATE statement_drafts SET last_revised_at_utc_ticks = 300 WHERE candidate_id = @id", ("id", StatementId));
        await seeder.ActionAsync(RelationshipFixtures.Action(), "AwaitingReview", revisedAtTicks: 200);
        await seeder.VoteAsync(RelationshipFixtures.Vote(), "AwaitingReview", revisedAtTicks: 200);
    }
}
