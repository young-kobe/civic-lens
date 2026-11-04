using System.Collections.Immutable;
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

        await Assert.ThrowsAsync<StaleApprovalException>(() => DecideAsync(1, 0, ReviewDecisionKind.Approve));

        Assert.Equal("0", await ReadAsync("SELECT count(*) FROM relationship_review_decisions"));
        var read = (await reviews.GetAsync(recordId, default))!;
        Assert.True(read.CitedRecordsChanged);
        Assert.Equal(change.StartsWith("statement", StringComparison.Ordinal), read.StatementChanged);
        Assert.Equal(change.StartsWith("target", StringComparison.Ordinal), read.TargetChanged);
    }

    [Theory]
    [InlineData("statement")]
    [InlineData("target")]
    public async Task ReversingARejectionAfterACitedRecordChangedIsRefusedWithTheStepsThatFixItBecauseThePageMustSayWhatToDo(string change)
    {
        _ = await DecideAsync(1, 0, ReviewDecisionKind.Reject, reason: ReviewReasonCode.NotRelated);
        await ChangeAsync(change);

        var refused = await Assert.ThrowsAsync<StaleApprovalException>(() => DecideAsync(1, 1, ReviewDecisionKind.Approve));

        Assert.Contains("relationships draft refresh", refused.Message, StringComparison.Ordinal);
        Assert.Contains("Request changes", refused.Message, StringComparison.Ordinal);
        Assert.Equal("Rejected", await ReadAsync($"SELECT review_status FROM relationship_drafts WHERE record_id = '{recordId}'"));
    }

    [Theory]
    [InlineData("statement")]
    [InlineData("target")]
    public async Task ASaveWhoseApprovalFailsBecauseACitedRecordChangedWritesNoRevisionAndNoDecision(string change)
    {
        await ChangeAsync(change);
        var request = new SaveRelationshipDraftRequest(recordId, 1, RelationshipType.ClaimsCreditFor, "She claims credit for the bill.",
            Span(BillSentence), "F1", "save-and-approve", new ApprovalWithSave(0, null, [], null));

        await Assert.ThrowsAsync<StaleApprovalException>(() => new SaveRelationshipDraft(reviews).ExecuteAsync(Reviewer, request));

        Assert.Equal(("1", "0"), (await ReadAsync("SELECT count(*) FROM relationship_revisions"),
            await ReadAsync("SELECT count(*) FROM relationship_review_decisions")));
        Assert.Equal(1, (await reviews.GetAsync(recordId, default))!.Review.CurrentRevision.RevisionNumber);
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
        await Assert.ThrowsAsync<StaleApprovalException>(() => approving);
        Assert.Equal("0", await ReadAsync("SELECT count(*) FROM relationship_review_decisions"));
    }

    [Fact]
    public async Task ANoLongerMatchingPairShowsTheFlagInTheRead()
    {
        await seeder.ExecuteAsync("UPDATE relationship_drafts SET no_longer_matches = true");

        Assert.True((await reviews.GetAsync(recordId, default))!.Review.NoLongerMatches);
    }

    [Fact]
    public async Task AnApprovedLinkStaysApprovedWhenACitedRecordChangesAndOnlyAPersonCanWithdraw()
    {
        _ = await DecideAsync(1, 0, ReviewDecisionKind.Approve);

        await ChangeAsync("statement");

        var read = (await reviews.GetAsync(recordId, default))!;
        Assert.Equal((ReviewStatus.Approved, true), (read.Review.CurrentStatus, read.CitedRecordsChanged));
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
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new SaveRelationshipDraft(null!).ExecuteAsync(outsider,
            new(recordId, 1, RelationshipType.StatesPositionOn, "Reason.", Span(BillSentence), "F1", "save-1")));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new DecideRelationshipReview(reviews).ExecuteAsync(analysis,
            new(recordId, 1, 0, ReviewDecisionKind.Approve, null, [], "decide-1")));
    }

    [Fact]
    public async Task SaveAndApproveClosesTheListedReportsOfAnOlderRevisionAndNoOtherBecauseTheReviewerSawOnlyThose()
    {
        var listed = await OpenReportAsync(1);
        var unlisted = await OpenReportAsync(1);
        var elsewhere = await OpenReportAsync(1, new string('b', 64));
        var request = new SaveRelationshipDraftRequest(recordId, 1, RelationshipType.ClaimsCreditFor, "She claims credit for the bill.",
            Span(BillSentence), "F1", "save-and-approve", new ApprovalWithSave(0, null, [], null, [listed, elsewhere]));

        _ = await new SaveRelationshipDraft(reviews).ExecuteAsync(Reviewer, request);

        Assert.Equal(("CorrectionNeeded", "Approved with a correction.", Reviewer.Subject),
            (await ReportAsync(listed, "status"), await ReportAsync(listed, "note"), await ReportAsync(listed, "resolved_by")));
        Assert.Equal(("Open", "Open"), (await ReportAsync(unlisted, "status"), await ReportAsync(elsewhere, "status")));
    }

    [Fact]
    public async Task ASaveAndApproveWithNoEditClosesAReportOfAnOlderRevisionAndKeepsOneOnTheApprovedRevisionBecauseTheApprovedWordingIsNewerThanTheFirst()
    {
        _ = await SaveAsync(1, RelationshipType.StatesPositionOn, "Person reason.", Span(BillSentence), "F1");
        var older = await OpenReportAsync(1);
        var onApproved = await OpenReportAsync(2);

        _ = await SaveAndApproveUnchangedAsync([older, onApproved]);

        Assert.Equal(("CorrectionNeeded", "Open"), (await ReportAsync(older, "status"), await ReportAsync(onApproved, "status")));
        Assert.Equal("2", await ReadAsync("SELECT count(*) FROM relationship_revisions"));
    }

    [Fact]
    public async Task AForgedIdOfADismissedOrAlreadyCorrectedReportChangesNothingBecauseOnlyOpenReportsClose()
    {
        _ = await SaveAsync(1, RelationshipType.StatesPositionOn, "Person reason.", Span(BillSentence), "F1");
        var dismissed = await ResolvedReportAsync("Dismissed", "NULL");
        var corrected = await ResolvedReportAsync("CorrectionNeeded", "'First note.'");
        var before = await ReadAsync("SELECT string_agg(status || coalesce(note, '') || coalesce(resolved_by, '') || coalesce(resolved_at_utc_ticks, 0) || version, ',' ORDER BY report_id) FROM reader_reports");

        _ = await SaveAndApproveUnchangedAsync([dismissed, corrected]);

        Assert.Equal(before, await ReadAsync("SELECT string_agg(status || coalesce(note, '') || coalesce(resolved_by, '') || coalesce(resolved_at_utc_ticks, 0) || version, ',' ORDER BY report_id) FROM reader_reports"));
    }

    [Fact]
    public async Task ARejectedApprovalLeavesTheReportOpenBecauseTheCloseSitsInTheApprovalTransaction()
    {
        _ = await SaveAsync(1, RelationshipType.StatesPositionOn, "Person reason.", Span(BillSentence), "F1");
        var report = await OpenReportAsync(1);
        await seeder.ExecuteAsync("""
            CREATE FUNCTION refuse_idempotency_row() RETURNS trigger AS $$ BEGIN RAISE EXCEPTION 'refused'; END $$ LANGUAGE plpgsql;
            CREATE TRIGGER refuse_idempotency_row BEFORE INSERT ON document_change_review_idempotency
                FOR EACH ROW EXECUTE FUNCTION refuse_idempotency_row();
            """);

        await Assert.ThrowsAnyAsync<Exception>(() => SaveAndApproveUnchangedAsync([report]));

        Assert.Equal(("Open", "0"), (await ReportAsync(report, "status"), await ReadAsync("SELECT count(*) FROM relationship_review_decisions")));
    }

    [Fact]
    public async Task ASubjectWhoCannotReviewClosesNoReportBecauseTheReviewerCheckComesBeforeTheStore()
    {
        var report = await OpenReportAsync(1);
        var outsider = new ReviewActor("auth0|nobody", (ReviewRole)99);
        var save = new SaveRelationshipDraftRequest(recordId, 1, RelationshipType.StatesPositionOn, "Reason.", Span(BillSentence), "F1",
            "save-and-approve", new ApprovalWithSave(0, null, [], null, [report]));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new SaveRelationshipDraft(reviews).ExecuteAsync(outsider, save));

        Assert.Equal("Open", await ReportAsync(report, "status"));
    }

    private Task<RelationshipRevision> SaveAndApproveUnchangedAsync(ImmutableArray<Guid> reportIds) =>
        new SaveRelationshipDraft(reviews).ExecuteAsync(Reviewer, new SaveRelationshipDraftRequest(recordId, 2, RelationshipType.StatesPositionOn,
            "Person reason.", Span(BillSentence), "F1", Guid.NewGuid().ToString("N"), new ApprovalWithSave(0, null, [], null, reportIds)));

    private async Task<Guid> ResolvedReportAsync(string status, string note)
    {
        var id = Guid.NewGuid();
        await seeder.ExecuteAsync($"""
            INSERT INTO reader_reports (report_id, record_id, kind, revision_number, category, body, received_at_utc_ticks, status, note,
                resolved_by, resolved_at_utc_ticks, version)
            VALUES (@id, @record, 'Relationship', 1, 'other', 'text', 1, '{status}', {note}, 'auth0|first', 5, 1)
            """, ("id", id), ("record", recordId));
        return id;
    }

    private async Task<Guid> OpenReportAsync(int revision, string? record = null)
    {
        var id = Guid.NewGuid();
        await seeder.ExecuteAsync("""
            INSERT INTO reader_reports (report_id, record_id, kind, revision_number, category, body, received_at_utc_ticks, status, version)
            VALUES (@id, @record, 'Relationship', @revision, 'other', 'text', 1, 'Open', 0)
            """, ("id", id), ("record", record ?? recordId), ("revision", revision));
        return id;
    }

    private Task<string> ReportAsync(Guid id, string column) => ReadAsync($"SELECT {column} FROM reader_reports WHERE report_id = '{id}'");

    private async Task DraftAsync(IRelationshipDraftModel model)
    {
        _ = await drafts.RefreshPairsAsync(default);
        await AnalysisWorkers.RelationshipDrafting(drafts, new PostgresAnalysisStatusStore(factory, RelationshipDraftTask.Task), model,
            new RelationshipDraftSettings(10_000_000, concurrency: 1), Officials, TimeProvider.System).ExecuteAsync(once: true);
    }

    private Task<RelationshipRevision> SaveAsync(int expected, RelationshipType type, string reason, SourceSpan quote, string factId,
        string key = "save-2") =>
        new SaveRelationshipDraft(reviews).ExecuteAsync(Reviewer, new(recordId, expected, type, reason, quote, factId, key));

    private Task<ReviewDecision> DecideAsync(int revision, int stateVersion, ReviewDecisionKind kind, string? note = null,
        ReviewReasonCode? reason = null) =>
        new DecideRelationshipReview(reviews).ExecuteAsync(Reviewer, new(recordId, revision, stateVersion, kind, note, [],
            Guid.NewGuid().ToString("N"), ReasonCode: reason));

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
