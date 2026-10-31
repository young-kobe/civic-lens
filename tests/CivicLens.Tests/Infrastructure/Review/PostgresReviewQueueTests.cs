using System.Data.Common;
using CivicLens.Application.Paging;
using CivicLens.Application.Review;
using CivicLens.Core.Relationships;
using CivicLens.Core.Review;
using CivicLens.Infrastructure.Analysis;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Review;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;

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

    [Fact]
    public async Task OneRecordWhoseStoredRevisionCannotBeReadIsMarkedUnreadableAndTheOtherRowsStillLoad()
    {
        await SeedThreeKindsAsync();
        await seeder.ExecuteAsync("UPDATE vote_revisions SET revision_json = '{broken' WHERE record_id = @id", ("id", VoteId));

        var page = await store.ListAsync(null, 10, default);

        Assert.Equal([StatementId, VoteId, ActionId], page.Items.Select(item => item.RecordId));
        Assert.Equal(3, page.Total);
        var broken = page.Items.Single(item => item.RecordId == VoteId);
        Assert.True(broken.IsUnreadable);
        Assert.NotNull(broken.UnreadableCause);
        Assert.Equal(PublishedRecordKind.Vote, broken.Kind);
        Assert.Equal("Warren on rent", page.Items.Single(item => item.RecordId == StatementId).Headline);
        Assert.False(page.Items.Single(item => item.RecordId == ActionId).IsUnreadable);
    }

    [Fact]
    public async Task OneDecisionWithAnImpossibleTimeMarksOnlyItsRecordUnreadableAndTheOtherRowsStillLoad()
    {
        await SeedThreeKindsAsync();
        await seeder.ExecuteAsync("""
            SET session_replication_role = replica;
            UPDATE vote_drafts SET review_state_version = 1 WHERE record_id = @id;
            INSERT INTO vote_review_decisions (decision_id, record_id, revision_number, review_state_version, decision_json, kind, actor_subject, note, created_at_utc_ticks)
            VALUES (repeat('b', 32), @id, 1, 1, '{}', 'RequestChanges', 'auth0|reviewer', 'Check it.', -1)
            """, ("id", VoteId));

        var page = await store.ListAsync(null, 10, default);

        Assert.Equal(3, page.Total);
        Assert.True(page.Items.Single(item => item.RecordId == VoteId).IsUnreadable);
        Assert.False(page.Items.Single(item => item.RecordId == StatementId).IsUnreadable);
        Assert.False(page.Items.Single(item => item.RecordId == ActionId).IsUnreadable);
    }

    [Fact]
    public async Task ADatabaseFailureWhileReadingTheRevisionRowsReachesTheCallerEvenWhenItsTypeIsOneOfTheUnreadableDataTypes()
    {
        await SeedThreeKindsAsync();
        await seeder.ExecuteAsync("UPDATE vote_revisions SET revision_json = '{broken' WHERE record_id = @id", ("id", VoteId));
        var failure = new InvalidOperationException("The database failed.");
        var counter = new RevisionQueryInterceptor("vote_revisions", failure);
        var counted = CountedStore(counter);

        var thrown = await Assert.ThrowsAnyAsync<Exception>(() => counted.ListAsync(null, 10, default));

        Assert.Same(failure, thrown);
    }

    [Fact]
    public async Task TheQueueReadsTheSameNumberOfQueriesWhetherOneDraftIsUnreadableOrSeveralAre()
    {
        await SeedThreeKindsAsync();
        var counter = new RevisionQueryInterceptor("no_such_table", null);
        var counted = CountedStore(counter);
        await seeder.ExecuteAsync("UPDATE vote_revisions SET revision_json = '{broken' WHERE record_id = @id", ("id", VoteId));
        await counted.ListAsync(null, 10, default);
        counter.QueryCount = 0;

        var withOneUnreadable = await counted.ListAsync(null, 10, default);
        var queriesForOne = counter.QueryCount;
        await seeder.ExecuteAsync("UPDATE action_revisions SET revision_json = '{broken' WHERE record_id = @id", ("id", ActionId));
        await seeder.ExecuteAsync("UPDATE statement_revisions SET revision_json = '{broken' WHERE candidate_id = @id", ("id", StatementId));
        counter.QueryCount = 0;
        var withThreeUnreadable = await counted.ListAsync(null, 10, default);

        Assert.Equal(1, withOneUnreadable.Items.Count(item => item.IsUnreadable));
        Assert.All(withThreeUnreadable.Items, item => Assert.True(item.IsUnreadable));
        Assert.Equal(queriesForOne, counter.QueryCount);
    }

    [Theory]
    [InlineData("jsonb_set(revision_json::jsonb, '{candidateId}', '\"ffff\"')::text")]
    [InlineData("'{}'")]
    public async Task ACitedStatementWhoseStoredJsonDoesNotMatchItsCitationMarksItsRelationshipUnreadableAndNamesTheCitation(string replacement)
    {
        await SeedEveryKindAsync();
        await seeder.ExecuteAsync($"UPDATE statement_revisions SET revision_json = {replacement} WHERE candidate_id = @id", ("id", StatementId));

        var page = await store.ListAsync(null, 10, default);

        var relationship = Of(page, PublishedRecordKind.Relationship);
        Assert.True(relationship.IsUnreadable);
        Assert.Contains(StatementId, relationship.UnreadableCause!.Message);
        Assert.Contains("revision 1", relationship.UnreadableCause.Message);
        Assert.False(Of(page, PublishedRecordKind.Vote).IsUnreadable);
    }

    [Fact]
    public async Task ABrokenCitedStatementMarksOnlyItsRelationshipUnreadableAndTheOtherRowsStillLoad()
    {
        await SeedEveryKindAsync();
        await seeder.ExecuteAsync("UPDATE statement_revisions SET revision_json = '{broken' WHERE candidate_id = @id", ("id", StatementId));

        var page = await store.ListAsync(null, 10, default);

        var relationship = Of(page, PublishedRecordKind.Relationship);
        Assert.True(relationship.IsUnreadable);
        Assert.True(Of(page, PublishedRecordKind.Statement).IsUnreadable);
        Assert.Equal(7, page.Items.Length);
        Assert.Equal(RelationshipFixtures.Action().Headline, Of(page, PublishedRecordKind.Action).Headline);
        Assert.False(Of(page, PublishedRecordKind.Vote).IsUnreadable);
    }

    [Fact]
    public async Task ACitedStatementRevisionThatIsNotStoredMarksOnlyItsRelationshipUnreadable()
    {
        await SeedEveryKindAsync();
        await seeder.ExecuteAsync("DELETE FROM statement_revisions WHERE candidate_id = @id", ("id", StatementId));

        var page = await store.ListAsync(null, 10, default);

        Assert.True(Of(page, PublishedRecordKind.Relationship).IsUnreadable);
        Assert.Equal(RelationshipFixtures.Action().Headline, Of(page, PublishedRecordKind.Action).Headline);
    }

    [Fact]
    public async Task SearchFindsApprovedRecordsByHeadlineWordsOfEveryKindThatHasOneAndSkipsRecordsThatWaitForAPersonAndOldRevisions()
    {
        var approvedIds = await SeedApprovedRecordsOfSixKindsAsync();
        await seeder.ExecuteAsync("""
            UPDATE statement_revisions SET revision_json = jsonb_set(revision_json::jsonb, '{headline}', '"Zebra statement"')::text;
            UPDATE action_revisions SET revision_json = jsonb_set(revision_json::jsonb, '{headline}', '"Zebra action"')::text;
            UPDATE vote_revisions SET revision_json = jsonb_set(revision_json::jsonb, '{headline}', '"Zebra vote"')::text;
            UPDATE brief_revisions SET revision_json = jsonb_set(revision_json::jsonb, '{headline}', '"Zebra brief"')::text;
            UPDATE executive_action_revisions SET revision_json = jsonb_set(revision_json::jsonb, '{title}', '"Zebra order"')::text;
            """);
        var waiting = RelationshipFixtures.Action(5) with { RecordId = new('e', 64), CandidateId = new('e', 64), Headline = "Zebra waits" };
        await seeder.ActionAsync(waiting, "AwaitingReview", revisedAtTicks: 500);
        var replaced = RelationshipFixtures.Action(6, revision: 1) with { RecordId = new('f', 64), CandidateId = new('f', 64), Headline = "Zebra old" };
        await seeder.ActionAsync(replaced, "Approved", revisedAtTicks: 600);
        await seeder.ActionAsync(replaced with { RevisionNumber = 2, Headline = "Quiet new" }, "Approved", revisedAtTicks: 600);

        var hits = await store.SearchApprovedAsync("zebra", null, 20, default);
        var none = await store.SearchApprovedAsync("zzzunmatched", null, 10, default);

        Assert.Equal(approvedIds.Order(), hits.Items.Select(item => (item.Kind, item.RecordId)).Order());
        Assert.Equal(6, hits.Items.Select(item => item.Kind).Distinct().Count());
        Assert.Empty(none.Items);
    }

    [Fact]
    public async Task SearchPagesWalkEveryMatchOnce()
    {
        await seeder.StatementAsync(RelationshipFixtures.Statement(dated: false), "Approved");
        var action = RelationshipFixtures.Action(5) with { RecordId = new('d', 64), CandidateId = new('d', 64) };
        await seeder.ActionAsync(action, "Approved", revisedAtTicks: 900);
        await seeder.ExecuteAsync("UPDATE action_revisions SET revision_json = replace(revision_json, @old, 'Warren on rent') WHERE record_id = @id",
            ("old", action.Headline), ("id", action.RecordId));

        var first = await store.SearchApprovedAsync("rent", null, 1, default);
        var second = await store.SearchApprovedAsync("rent", PageCursor.Parse(first.OlderCursor), 1, default);
        var back = await store.SearchApprovedAsync("rent", PageCursor.Parse(second.NewerCursor), 1, default);

        Assert.Equal([action.RecordId, StatementId], new[] { first, second }.Select(page => page.Items.Single().RecordId));
        Assert.Null(second.OlderCursor);
        Assert.Equal(action.RecordId, back.Items.Single().RecordId);
    }

    private async Task<(PublishedRecordKind Kind, string RecordId)[]> SeedApprovedRecordsOfSixKindsAsync()
    {
        await seeder.StatementAsync(RelationshipFixtures.Statement(dated: false), "Approved");
        await seeder.ActionAsync(RelationshipFixtures.Action(), "Approved", revisedAtTicks: 200);
        await seeder.VoteAsync(RelationshipFixtures.Vote(), "Approved", revisedAtTicks: 200);
        var documentChangeId = new string('d', 32);
        await seeder.ExecuteAsync("""
            SET session_replication_role = replica;
            INSERT INTO document_change_drafts (draft_id, comparison_id, current_revision_number, review_state_version, review_status, created_at_utc_ticks, last_revised_at_utc_ticks)
            VALUES (@id, repeat('c', 64), 1, 0, 'Approved', 0, 100)
            """, ("id", documentChangeId));
        var briefs = new BriefWorldSeeder(connectionString);
        await briefs.DocumentChangeAsync(documentChangeId, 1, "Zebra change");
        var brief = BriefFixtures.ReviewableRevision();
        await briefs.ReviewableBriefAsync(brief, "Approved");
        var executiveId = await new ExecutiveActionSeeder(connectionString, captureRoot)
            .AdmitAsync(FederalRegisterFixtures.TrumpOrder, "donald-trump");
        return
        [
            (PublishedRecordKind.Statement, StatementId), (PublishedRecordKind.Action, ActionId), (PublishedRecordKind.Vote, VoteId),
            (PublishedRecordKind.DocumentChange, documentChangeId), (PublishedRecordKind.Brief, brief.RecordId),
            (PublishedRecordKind.ExecutiveAction, executiveId)
        ];
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

    private PostgresReviewQueueStore CountedStore(RevisionQueryInterceptor interceptor) => new(new PooledDbContextFactory<CollectionAttemptDbContext>(
        new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).AddInterceptors(interceptor).Options));

    private sealed class RevisionQueryInterceptor(string table, Exception? failure) : DbCommandInterceptor
    {
        public int QueryCount { get; set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            QueryCount++;
            if (failure is not null && command.CommandText.Contains(table, StringComparison.Ordinal)) throw failure;
            return ValueTask.FromResult(result);
        }
    }
}
