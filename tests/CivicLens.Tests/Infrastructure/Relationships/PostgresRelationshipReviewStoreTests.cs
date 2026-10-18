using CivicLens.Application.Relationships;
using CivicLens.Application.Review;
using CivicLens.Core.Documents;
using CivicLens.Core.Relationships;
using CivicLens.Core.Review;
using CivicLens.Infrastructure.Analysis;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Relationships;
using CivicLens.Tests.Application.Relationships;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using static CivicLens.Tests.Fixtures.RelationshipFixtures;

namespace CivicLens.Tests.Infrastructure.Relationships;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PostgresRelationshipReviewStoreTests(PostgresCollection postgres) : IAsyncLifetime
{
    private static readonly ReviewActor Reviewer = new("auth0|reviewer", ReviewRole.Reviewer);

    private string connectionString = null!;
    private PooledDbContextFactory<CollectionAttemptDbContext> factory = null!;
    private PostgresRelationshipDraftStore drafts = null!;
    private PostgresRelationshipReviewStore reviews = null!;
    private RelationshipWorldSeeder seeder = null!;
    private string recordId = null!;

    public async Task InitializeAsync()
    {
        connectionString = await postgres.CreateMigratedDatabaseAsync();
        factory = new PooledDbContextFactory<CollectionAttemptDbContext>(
            new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options);
        drafts = new PostgresRelationshipDraftStore(factory);
        reviews = new PostgresRelationshipReviewStore(factory);
        seeder = new RelationshipWorldSeeder(connectionString);
        await seeder.StatementAsync(Statement());
        await seeder.ActionAsync(Action());
        recordId = ActionPair().PairId;
        await DraftAsync(new FakeRelationshipDraftModel().Draft(Output(quote: BillWords)));
    }

    public Task DisposeAsync() => postgres.DropDatabaseAsync(connectionString);

    [Fact]
    public async Task APersonSaveChangesTheTypeReasonQuoteAndFactAndLeavesThePairAndTheMatchAlone()
    {
        var before = (await reviews.GetRevisionAsync(recordId, 1, default))!;
        var passage = Span(BillSentence);

        var saved = await SaveAsync(1, RelationshipType.ClaimsCreditFor, "She claims credit for the bill.", passage, "F1");

        Assert.Equal((2, RelationshipType.ClaimsCreditFor, "She claims credit for the bill.", passage, "F1"),
            (saved.RevisionNumber, saved.Type, saved.Reason, saved.StatementQuote, saved.TargetFactId));
        Assert.Equal((before.Statement, before.Target, before.Match), (saved.Statement, saved.Target, saved.Match));
        Assert.Equal(("Person", "AwaitingReview"), (await ReadAsync("SELECT author_kind FROM relationship_revisions WHERE revision_number = 2"),
            await ReadAsync($"SELECT review_status FROM relationship_drafts WHERE record_id = '{recordId}'")));
    }

    [Fact]
    public async Task AQuoteThatNoPassageOffersOrAFactTheTargetDoesNotListIsRefusedAndNoRevisionIsSaved()
    {
        var start = Article.IndexOf("said: ", StringComparison.Ordinal);
        var crossesThePassageStart = new SourceSpan(ExtractionId, start, Article.IndexOf(BillWords, StringComparison.Ordinal) + BillWords.Length - start);
        var insideOnePassageButNotOffered = Span(BillSentence) with { Start = Span(BillSentence).Start + 1, Length = Span(BillSentence).Length - 1 };
        var tooShortForTheMatch = Span(BillWords) with { Length = 3 };

        await Assert.ThrowsAsync<ArgumentException>(() => SaveAsync(1, RelationshipType.StatesPositionOn, "Reason.", crossesThePassageStart, "F1"));
        await Assert.ThrowsAsync<ArgumentException>(() => SaveAsync(1, RelationshipType.StatesPositionOn, "Reason.", tooShortForTheMatch, "F1"));
        await Assert.ThrowsAsync<ArgumentException>(() => SaveAsync(1, RelationshipType.StatesPositionOn, "Reason.", insideOnePassageButNotOffered, "F1"));
        await Assert.ThrowsAsync<ArgumentException>(() => SaveAsync(1, RelationshipType.StatesPositionOn, "Reason.", Span(BillSentence), "F9"));

        Assert.Equal("1", await ReadAsync($"SELECT current_revision_number FROM relationship_drafts WHERE record_id = '{recordId}'"));
    }

    [Fact]
    public async Task ATypeThatDoesNotFitTheTargetKindIsRefused()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => SaveAsync(1, RelationshipType.StatementAboutVote, "Reason.", Span(BillSentence), "F1"));
    }

    [Fact]
    public async Task AStaleSaveIsAConflictAndKeepsTheNewerRevision()
    {
        _ = await SaveAsync(1, RelationshipType.StatesPositionOn, "First person reason.", Span(BillSentence), "F1");

        await Assert.ThrowsAsync<ReviewConflictException>(() =>
            SaveAsync(1, RelationshipType.StatesPositionOn, "Stale person reason.", Span(BillSentence), "F1", "save-3"));

        Assert.Equal("First person reason.", (await reviews.GetAsync(recordId, default))!.Review.CurrentRevision.Reason);
    }

    [Fact]
    public async Task ApprovalBindsToTheExactRevisionSoASaveAfterThePageLoadedTurnsItIntoAConflict()
    {
        _ = await SaveAsync(1, RelationshipType.StatesPositionOn, "Person reason.", Span(BillSentence), "F1");

        await Assert.ThrowsAsync<ReviewConflictException>(() => DecideAsync(1, 0, ReviewDecisionKind.Approve));
        _ = await DecideAsync(2, 0, ReviewDecisionKind.Approve);

        Assert.Equal("Approved", await ReadAsync($"SELECT review_status FROM relationship_drafts WHERE record_id = '{recordId}'"));
        Assert.Equal("2", await ReadAsync("SELECT revision_number FROM relationship_review_decisions"));
    }

    [Theory]
    [InlineData("statement")]
    [InlineData("target")]
    [InlineData("statementNotApproved")]
    [InlineData("targetNotApproved")]
    public async Task ApprovalIsRefusedWhenACitedRevisionIsNoLongerTheApprovedRevisionOfItsRecord(string change)
    {
        await ChangeAsync(change);

        await Assert.ThrowsAsync<ArgumentException>(() => DecideAsync(1, 0, ReviewDecisionKind.Approve));

        Assert.Equal("0", await ReadAsync("SELECT count(*) FROM relationship_review_decisions"));
        var read = (await reviews.GetAsync(recordId, default))!;
        Assert.True(read.CitedRecordsChanged);
        Assert.Equal(change.StartsWith("statement", StringComparison.Ordinal), read.StatementChanged);
        Assert.Equal(change.StartsWith("target", StringComparison.Ordinal), read.TargetChanged);
        Assert.True(Assert.Single((await reviews.ListAsync(null, 10, DraftStatusFilter.All, default)).Items).CitedRecordsChanged);
    }

    [Fact]
    public async Task TheApprovalGateReadsTheCitedRecordsAfterItTakesTheRelationshipLockSoAStatementApprovalThatCommitsFirstBlocksIt()
    {
        await using var holder = await HeldAdvisoryLock.AcquireAsync(connectionString, "civic-lens-relationship-review");

        var approving = DecideAsync(1, 0, ReviewDecisionKind.Approve);
        await HeldAdvisoryLock.WaitForWaiterAsync(connectionString, "civic-lens-relationship-review");
        await seeder.StatementAsync(Statement(revision: 2));

        Assert.False(approving.IsCompleted);
        await holder.DisposeAsync();
        await Assert.ThrowsAsync<ArgumentException>(() => approving);
        Assert.Equal("0", await ReadAsync("SELECT count(*) FROM relationship_review_decisions"));
    }

    [Fact]
    public async Task ANoLongerMatchingPairShowsTheFlagInTheReadAndTheList()
    {
        await seeder.ExecuteAsync("UPDATE relationship_drafts SET no_longer_matches = true");

        Assert.True((await reviews.GetAsync(recordId, default))!.Review.NoLongerMatches);
        Assert.True(Assert.Single((await reviews.ListAsync(null, 10, DraftStatusFilter.All, default)).Items).Review.NoLongerMatches);
    }

    [Fact]
    public async Task AnApprovedLinkStaysApprovedWhenACitedRecordChangesAndOnlyAPersonCanWithdraw()
    {
        _ = await DecideAsync(1, 0, ReviewDecisionKind.Approve);

        await ChangeAsync("statement");

        var item = Assert.Single((await reviews.ListAsync(null, 10, DraftStatusFilter.Approved, default)).Items);
        Assert.Equal((ReviewStatus.Approved, true), (item.Review.CurrentStatus, item.CitedRecordsChanged));
        _ = await DecideAsync(1, 1, ReviewDecisionKind.WithdrawApproval, "A cited record changed.");
        Assert.Equal("ApprovalWithdrawn", await ReadAsync($"SELECT review_status FROM relationship_drafts WHERE record_id = '{recordId}'"));
    }

    [Fact]
    public async Task TheReadShowsTheCitedRevisionAndTheFactsOfThatRevisionNotTheNewerApprovedOne()
    {
        await seeder.ActionAsync(Action(revision: 2) with { Headline = "Newer action headline" });

        var read = (await reviews.GetAsync(recordId, default))!;

        Assert.Equal((1, true), (read.CitedTarget.RevisionNumber, read.TargetChanged));
        Assert.DoesNotContain("Newer action headline", read.CitedTarget.Facts.Select(fact => fact.Text));
        Assert.Equal([Span(BillWords), Span(BillSentence)], read.Options.Quotes.Select(quote => quote.Span));
        Assert.Equal(read.CitedTarget.Facts, read.Options.Facts);
    }

    [Fact]
    public async Task TheListShowsTheHeadlinesOfBothSidesAndTheCountsFollowTheStatusRule()
    {
        var item = Assert.Single((await reviews.ListAsync(null, 10, DraftStatusFilter.NeedsAction, default)).Items);
        Assert.Equal(("Warren on rent", Action().Headline, false), (item.StatementHeadline, item.TargetHeadline, item.CitedRecordsChanged));
        Assert.Equal(new RelationshipReviewCounts(1, 0), await reviews.CountAsync(default));

        _ = await DecideAsync(1, 0, ReviewDecisionKind.Approve);

        Assert.Equal(new RelationshipReviewCounts(0, 1), await reviews.CountAsync(default));
        Assert.Empty((await reviews.ListAsync(null, 10, DraftStatusFilter.NeedsAction, default)).Items);
        Assert.Single((await reviews.ListAsync(null, 10, DraftStatusFilter.Approved, default)).Items);
    }

    [Fact]
    public async Task ARelationshipThatDoesNotExistIsNotFoundAndSavingOrDecidingOnItFailsLoud()
    {
        var missing = new string('9', 64);

        Assert.Null(await new GetRelationshipReview(reviews).ExecuteAsync(Reviewer, missing));
        await Assert.ThrowsAsync<ArgumentException>(() => new GetRelationshipReview(reviews).ExecuteAsync(Reviewer, "not-a-record"));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => new SaveRelationshipDraft(reviews).ExecuteAsync(Reviewer,
            new(missing, 1, RelationshipType.StatesPositionOn, "Reason.", Span(BillSentence), "F1", "save-1")));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => new DecideRelationshipReview(reviews).ExecuteAsync(Reviewer,
            new(missing, 1, 0, ReviewDecisionKind.Approve, null, [], "decide-1")));
    }

    [Fact]
    public async Task OnlyAReviewerMayReadSaveOrDecideAndTheAnalysisAuthorNever()
    {
        var outsider = new ReviewActor("auth0|nobody", (ReviewRole)99);
        var analysis = new ReviewActor(ReviewAuthor.AnalysisSubject, ReviewRole.Owner);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new GetRelationshipReview(null!).ExecuteAsync(outsider, recordId));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new ListRelationshipReviews(null!).ExecuteAsync(outsider, null));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new GetRelationshipReviewCounts(null!).ExecuteAsync(outsider));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new SaveRelationshipDraft(null!).ExecuteAsync(outsider,
            new(recordId, 1, RelationshipType.StatesPositionOn, "Reason.", Span(BillSentence), "F1", "save-1")));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new DecideRelationshipReview(reviews).ExecuteAsync(analysis,
            new(recordId, 1, 0, ReviewDecisionKind.Approve, null, [], "decide-1")));
    }

    private async Task DraftAsync(IRelationshipDraftModel model)
    {
        _ = await drafts.RefreshPairsAsync(default);
        await AnalysisWorkers.RelationshipDrafting(drafts, new PostgresAnalysisStatusStore(factory, RelationshipDraftTask.Task), model,
            new RelationshipDraftSettings(10_000_000, concurrency: 1), Officials, TimeProvider.System).ExecuteAsync(once: true);
    }

    private Task<RelationshipRevision> SaveAsync(int expected, RelationshipType type, string reason, SourceSpan quote, string factId,
        string key = "save-2") =>
        new SaveRelationshipDraft(reviews).ExecuteAsync(Reviewer, new(recordId, expected, type, reason, quote, factId, key));

    private Task<ReviewDecision> DecideAsync(int revision, int stateVersion, ReviewDecisionKind kind, string? note = null) =>
        new DecideRelationshipReview(reviews).ExecuteAsync(Reviewer, new(recordId, revision, stateVersion, kind, note, [],
            Guid.NewGuid().ToString("N")));

    private async Task ChangeAsync(string change)
    {
        switch (change)
        {
            case "statement":
                await seeder.StatementAsync(Statement(revision: 2));
                break;
            case "target":
                await seeder.ActionAsync(Action(revision: 2));
                break;
            case "statementNotApproved":
                await seeder.SetStatusAsync("statement_drafts", "candidate_id", StatementId, "ApprovalWithdrawn");
                break;
            default:
                await seeder.SetStatusAsync("action_drafts", "record_id", ActionId, "ChangesRequested");
                break;
        }
    }

    private async Task<string> ReadAsync(string sql) => (await seeder.ReadAsync(sql)).Single().Single();
}
