using CivicLens.Application.Paging;
using CivicLens.Application.Review;
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
    private string connectionString = null!;
    private PostgresReviewQueueStore store = null!;
    private RelationshipWorldSeeder seeder = null!;

    public async Task InitializeAsync()
    {
        connectionString = await postgres.CreateMigratedDatabaseAsync();
        store = new PostgresReviewQueueStore(AnalysisDbContextFactory.Create(connectionString));
        seeder = new RelationshipWorldSeeder(connectionString);
    }

    public Task DisposeAsync() => postgres.DropDatabaseAsync(connectionString);

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
        Assert.Equal("Code did not approve this record. A person must decide.", page.Items.Single(item => item.RecordId == VoteId).Reason);
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
    public async Task EveryDraftTableFeedsTheQueue()
    {
        await seeder.StatementAsync(RelationshipFixtures.Statement(dated: false), "AwaitingReview");
        await seeder.ExecuteAsync("UPDATE statement_drafts SET last_revised_at_utc_ticks = 5 WHERE candidate_id = @id", ("id", StatementId));
        foreach (var kind in Enum.GetValues<PublishedRecordKind>().Where(kind => kind != PublishedRecordKind.Statement))
            await seeder.ExecuteAsync(DraftRowInserts.For(kind), ("id", new string("abcdef0"[(int)kind], kind == PublishedRecordKind.DocumentChange ? 32 : 64)));

        var page = await store.ListAsync(null, 1, default);

        Assert.Equal(7, page.Total);
    }

    private async Task SeedThreeKindsAsync()
    {
        await seeder.StatementAsync(RelationshipFixtures.Statement(dated: false), "AwaitingReview");
        await seeder.ExecuteAsync("UPDATE statement_drafts SET last_revised_at_utc_ticks = 300 WHERE candidate_id = @id", ("id", StatementId));
        await seeder.ActionAsync(RelationshipFixtures.Action(), "AwaitingReview", revisedAtTicks: 200);
        await seeder.VoteAsync(RelationshipFixtures.Vote(), "AwaitingReview", revisedAtTicks: 200);
    }
}
