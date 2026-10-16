using System.Collections.Immutable;
using CivicLens.Application.Briefs;
using CivicLens.Application.Review;
using CivicLens.Core.Briefs;
using CivicLens.Core.Review;
using CivicLens.Infrastructure.Analysis;
using CivicLens.Infrastructure.Briefs;
using CivicLens.Infrastructure.Collection;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Application.Briefs;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;
using static CivicLens.Tests.Fixtures.BriefFixtures;
using static CivicLens.Tests.Fixtures.BriefWorldSeeder;

namespace CivicLens.Tests.Infrastructure.Briefs;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PostgresBriefReviewStoreTests(PostgresCollection postgres) : IAsyncLifetime
{
    private static readonly ReviewActor Reviewer = new("auth0|reviewer", ReviewRole.Reviewer);
    private static readonly DateOnly OtherWeek = new(2026, 10, 12);

    private string connectionString = null!;
    private PooledDbContextFactory<CollectionAttemptDbContext> factory = null!;
    private PostgresBriefReviewStore reviews = null!;
    private PostgresBriefDraftStore drafts = null!;
    private BriefWorldSeeder seeder = null!;

    public async Task InitializeAsync()
    {
        connectionString = await postgres.CreateMigratedDatabaseAsync();
        factory = new PooledDbContextFactory<CollectionAttemptDbContext>(
            new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options);
        reviews = new PostgresBriefReviewStore(factory);
        drafts = new PostgresBriefDraftStore(factory);
        seeder = new BriefWorldSeeder(connectionString);
        await seeder.ReleaseCitedRecordsAsync();
        await seeder.ReviewableBriefAsync(ReviewableRevision());
    }

    public Task DisposeAsync() => postgres.DropDatabaseAsync(connectionString);

    [Fact]
    public async Task ApprovalBindsToTheExactBriefRevisionSoASaveAfterThePageLoadedTurnsItIntoAConflict()
    {
        _ = await SaveAsync(1, "Person headline", [new("A person paragraph.", ["R1", "R2", "R3"])], "save-2");

        await Assert.ThrowsAsync<ReviewConflictException>(() => DecideAsync(1, 0, ReviewDecisionKind.Approve));
        _ = await DecideAsync(2, 0, ReviewDecisionKind.Approve);

        Assert.Equal(2, await ScalarAsync("SELECT revision_number FROM brief_review_decisions"));
        Assert.Equal("Approved", await ScalarAsync($"SELECT review_status FROM brief_drafts WHERE record_id = '{RecordId}'"));
    }

    [Fact]
    public async Task AStaleSaveIsAConflictAndKeepsTheNewerRevisionSoThePageCanShowBothVersions()
    {
        _ = await SaveAsync(1, "First person headline", ReviewableRevision().Paragraphs, "save-2");

        await Assert.ThrowsAsync<ReviewConflictException>(() => SaveAsync(1, "Stale person headline", ReviewableRevision().Paragraphs, "save-3"));

        Assert.Equal(2, await ScalarAsync($"SELECT current_revision_number FROM brief_drafts WHERE record_id = '{RecordId}'"));
        Assert.Equal("First person headline", (await reviews.GetAsync(RecordId, default))!.Review.CurrentRevision.Headline);
    }

    [Fact]
    public async Task ASaveDropsACitationThatNoParagraphUsesAnymoreAndRecomputesTheCoverage()
    {
        var saved = await SaveAsync(1, "Person headline", [new("Only the statement.", ["R1"]), new("The rule.", ["R2"])], "save-2");

        Assert.Equal(["R1", "R2"], saved.Citations.Select(citation => citation.Id));
        Assert.Equal(new BriefCoverage(3, 3, 2), saved.Coverage);
        Assert.Equal(saved.Citations.ToArray(), (await reviews.GetRevisionAsync(RecordId, 2, default))!.Citations.ToArray());
        Assert.Equal(["R1", "R2"], (await reviews.GetAsync(RecordId, default))!.Cited.Keys.Order());
    }

    [Fact]
    public async Task APersonCannotCiteAnIdThatTheBriefDoesNotListSoTheHandlerRefusesTheSave()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => SaveAsync(1, "Person headline", [new("A paragraph.", ["R1", "R9"])], "save-2"));

        Assert.Equal(1, await ScalarAsync($"SELECT current_revision_number FROM brief_drafts WHERE record_id = '{RecordId}'"));
    }

    [Fact]
    public async Task TheStoreRefusesACitationThatTheCurrentRevisionDoesNotListEvenWhenTheEditIsOtherwiseWellFormed()
    {
        var current = ReviewableRevision();
        var foreign = new BriefCitation("R4", BriefCitationKind.Statement, StatementId('9'), 1);
        var forged = current with
        {
            RevisionNumber = 2,
            AuthorSubject = Reviewer.Subject,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            TextOrigin = ReviewTextOrigin.Person,
            Paragraphs = [.. current.Paragraphs, new("A foreign record.", ["R4"])],
            Citations = [.. current.Citations, foreign],
            Coverage = current.Coverage with { Cited = 4 }
        };
        forged.ValidateShape();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            reviews.SaveRevisionAsync(Reviewer.Subject, forged, 1, "save-2", new string('6', 64), default));

        Assert.Equal(1, await ScalarAsync($"SELECT current_revision_number FROM brief_drafts WHERE record_id = '{RecordId}'"));
    }

    [Fact]
    public async Task ARerunAfterAPersonEditKeepsTheTextOfThePersonAndStoresTheModelTextAsASuggestion()
    {
        var third = await PersonTextWithSuggestionAsync();

        Assert.Equal((3, ReviewTextOrigin.Person, "Person headline"), (third.RevisionNumber, third.TextOrigin, third.Headline));
        Assert.Equal(PersonParagraphs.ToArray(), third.Paragraphs.ToArray());
        Assert.Equal("Model headline", third.Suggestion!.Headline);
        Assert.Equal("AwaitingReview", await ScalarAsync($"SELECT review_status FROM brief_drafts WHERE record_id = '{RecordId}'"));
    }

    [Fact]
    public async Task APersonSaveMayKeepOrClearTheSuggestionButTheStoreRefusesAChangedOne()
    {
        var third = await PersonTextWithSuggestionAsync();

        var kept = await SaveAsync(3, "Person headline", PersonParagraphs, "save-4", SuggestionChoice.Keep);
        var cleared = await SaveAsync(4, "Person headline", PersonParagraphs, "save-5", SuggestionChoice.Dismiss);

        Assert.Equal(third.Suggestion, kept.Suggestion);
        Assert.Null(cleared.Suggestion);
        var forged = cleared with
        {
            RevisionNumber = 6,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            Suggestion = third.Suggestion! with { Headline = "A forged suggestion" }
        };
        await Assert.ThrowsAsync<ArgumentException>(() =>
            reviews.SaveRevisionAsync(Reviewer.Subject, forged, 5, "save-6", new string('6', 64), default));
        Assert.Equal(5, await ScalarAsync($"SELECT current_revision_number FROM brief_drafts WHERE record_id = '{RecordId}'"));
    }

    [Fact]
    public async Task AdoptingTheSuggestionSavesItsHeadlineParagraphsAndCitationsAsOneUnitAndClearsIt()
    {
        var third = await PersonTextWithSuggestionAsync();
        var suggestion = third.Suggestion!;

        var adopted = await SaveAsync(3, "Posted headline", PersonParagraphs, "save-4", SuggestionChoice.Use);

        Assert.Equal((4, ReviewTextOrigin.Person, suggestion.Headline), (adopted.RevisionNumber, adopted.TextOrigin, adopted.Headline));
        Assert.Equal(suggestion.Paragraphs.ToArray(), adopted.Paragraphs.ToArray());
        Assert.Equal(suggestion.Citations.ToArray(), adopted.Citations.ToArray());
        Assert.Equal(suggestion.Citations.Length, adopted.Coverage.Cited);
        Assert.Null(adopted.Suggestion);
        Assert.Null((await reviews.GetRevisionAsync(RecordId, 4, default))!.Suggestion);
    }

    [Fact]
    public async Task AdoptionRefusesTextThatIsNotTheSuggestionAndARevisionWithoutASuggestion()
    {
        var third = await PersonTextWithSuggestionAsync();
        var suggestion = third.Suggestion!;

        var forged = third with
        {
            RevisionNumber = 4,
            AuthorSubject = Reviewer.Subject,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            TextOrigin = ReviewTextOrigin.Person,
            Suggestion = null,
            Headline = suggestion.Headline,
            Paragraphs = suggestion.Paragraphs,
            Citations = [.. suggestion.Citations.Select(citation => citation with { RevisionNumber = citation.RevisionNumber + 1 })]
        };
        await Assert.ThrowsAsync<ArgumentException>(() =>
            reviews.SaveRevisionAsync(Reviewer.Subject, forged, 3, "save-6", new string('6', 64), default));
        Assert.Equal(3, await ScalarAsync($"SELECT current_revision_number FROM brief_drafts WHERE record_id = '{RecordId}'"));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            SaveAsync(1, "Headline", ReviewableRevision().Paragraphs, "save-7", SuggestionChoice.Use));
    }

    [Fact]
    public async Task AfterAWithdrawalAndARedraftAPersonRecoversABriefWhoseCitedRecordChangedByAdoptingTheSuggestion()
    {
        _ = await SaveAsync(1, "Person headline", PersonParagraphs, "save-2");
        _ = await DecideAsync(2, 0, ReviewDecisionKind.Approve);
        await ReplaceCitedStatementWithRevisionTwoAsync();
        _ = await DecideAsync(2, 1, ReviewDecisionKind.WithdrawApproval, "A cited record changed.");
        var third = await RedraftWithModelTextAsync();
        Assert.True((await reviews.GetAsync(RecordId, default))!.CitedRecordsChanged);
        var suggestion = third.Suggestion!;

        _ = await SaveAsync(3, suggestion.Headline, suggestion.Paragraphs, "save-4", SuggestionChoice.Use);

        var recovered = (await reviews.GetAsync(RecordId, default))!;
        Assert.False(recovered.CitedRecordsChanged);
        _ = await new DecideBriefReview(reviews).ExecuteAsync(Reviewer, new(RecordId, 4, recovered.Review.ReviewStateVersion,
            ReviewDecisionKind.Approve, "Adopted the new text.", [.. recovered.Review.UnresolvedConcerns.Select(concern => concern.DecisionId)], "approve-4"));
    }

    [Fact]
    public async Task TheApprovalGateRunsUnderTheBriefLockSoNoCitedRecordWriterCommitsBetweenTheGateAndTheApproval()
    {
        await using var holder = await HeldAdvisoryLock.AcquireAsync(connectionString, "civic-lens-brief-review");

        var approving = DecideAsync(1, 0, ReviewDecisionKind.Approve);
        await HeldAdvisoryLock.WaitForWaiterAsync(connectionString, "civic-lens-brief-review");
        await ChangeCitedStatementAsync("withdrawn");

        Assert.False(approving.IsCompleted);
        await holder.DisposeAsync();
        await Assert.ThrowsAsync<ArgumentException>(() => approving);
        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM brief_review_decisions"));
    }

    [Theory]
    [InlineData("withdrawn")]
    [InlineData("replaced")]
    [InlineData("absent")]
    public async Task ApprovalIsRefusedWhenACitedRevisionIsNotApprovedAndInTheActiveReleaseAndTheFlagNamesIt(string change)
    {
        await ChangeCitedStatementAsync(change);

        await Assert.ThrowsAsync<ArgumentException>(() => DecideAsync(1, 0, ReviewDecisionKind.Approve));

        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM brief_review_decisions"));
        var read = (await reviews.GetAsync(RecordId, default))!;
        Assert.True(read.CitedRecordsChanged);
        Assert.Equal([("R1", true), ("R2", false), ("R3", false)], read.Cited.OrderBy(pair => pair.Key).Select(pair => (pair.Key, pair.Value.Changed)));
        Assert.True(Assert.Single((await reviews.ListAsync(null, 10, DraftStatusFilter.All, default)).Items).CitedRecordsChanged);
    }

    [Fact]
    public async Task ApprovingTheCitedRecordAgainClearsTheFlagAndLetsTheBriefBeApproved()
    {
        await ChangeCitedStatementAsync("withdrawn");
        Assert.True((await reviews.GetAsync(RecordId, default))!.CitedRecordsChanged);

        await seeder.DecisionAsync(PublishedRecordKind.Statement, CitedStatement, 1, 3, ReviewDecisionKind.Approve);

        Assert.False((await reviews.GetAsync(RecordId, default))!.CitedRecordsChanged);
        _ = await DecideAsync(1, 0, ReviewDecisionKind.Approve);
    }

    [Fact]
    public async Task AnApprovedBriefStaysApprovedWhenACitedRecordChangesButTheFlagShowsItSoOnlyAPersonCanWithdraw()
    {
        _ = await DecideAsync(1, 0, ReviewDecisionKind.Approve);

        await ChangeCitedStatementAsync("withdrawn");

        var item = Assert.Single((await reviews.ListAsync(null, 10, DraftStatusFilter.Approved, default)).Items);
        Assert.Equal((ReviewStatus.Approved, true), (item.Review.CurrentStatus, item.CitedRecordsChanged));
        _ = await DecideAsync(1, 1, ReviewDecisionKind.WithdrawApproval, "A cited record changed.");
        Assert.Equal("ApprovalWithdrawn", await ScalarAsync($"SELECT review_status FROM brief_drafts WHERE record_id = '{RecordId}'"));
    }

    [Fact]
    public async Task TheListShowsDraftedBriefsOnlyAndCountsEachFilterWithTheSameStatusRule()
    {
        await seeder.QueueWeekAsync(new DateOnly(2026, 10, 19));
        await seeder.ReviewableBriefAsync(ReviewableRevision(OtherWeek));
        _ = await DecideAsync(1, 0, ReviewDecisionKind.Approve);

        var counts = await reviews.CountAsync(default);
        var all = await reviews.ListAsync(null, 10, DraftStatusFilter.All, default);
        var approved = await reviews.ListAsync(null, 10, DraftStatusFilter.Approved, default);
        var needsAction = await reviews.ListAsync(null, 10, DraftStatusFilter.NeedsAction, default);

        Assert.Equal(new BriefReviewCounts(1, 1), counts);
        Assert.Equal(2, all.Items.Length);
        Assert.Equal([RecordId], approved.Items.Select(item => item.Review.RecordId));
        Assert.Equal([BriefWeek.RecordIdOf(OtherWeek)], needsAction.Items.Select(item => item.Review.RecordId));
    }

    [Fact]
    public async Task ABriefThatDoesNotExistIsNotFoundAndSavingOrDecidingOnItFailsLoud()
    {
        var missing = new string('9', 64);

        Assert.Null(await reviews.GetAsync(missing, default));
        Assert.Null(await new GetBriefReview(reviews).ExecuteAsync(Reviewer, missing));
        await Assert.ThrowsAsync<ArgumentException>(() => new GetBriefReview(reviews).ExecuteAsync(Reviewer, "not-a-record"));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => new SaveBriefDraft(reviews).ExecuteAsync(Reviewer,
            new(missing, 1, "Headline", ReviewableRevision().Paragraphs, SuggestionChoice.Keep, "save-1")));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => new DecideBriefReview(reviews).ExecuteAsync(Reviewer,
            new(missing, 1, 0, ReviewDecisionKind.Approve, null, [], "decide-1")));
    }

    [Fact]
    public async Task OnlyAReviewerMayReadSaveOrDecideBecauseTheHandlersCheckTheRoleBeforeTouchingTheStore()
    {
        var outsider = new ReviewActor("auth0|nobody", (ReviewRole)99);
        var paragraphs = ReviewableRevision().Paragraphs;

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new GetBriefReview(null!).ExecuteAsync(outsider, RecordId));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new ListBriefReviews(null!).ExecuteAsync(outsider, null));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new GetBriefReviewCounts(null!).ExecuteAsync(outsider));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            new SaveBriefDraft(null!).ExecuteAsync(outsider, new(RecordId, 1, "Headline", paragraphs, SuggestionChoice.Keep, "save-1")));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            new DecideBriefReview(null!).ExecuteAsync(outsider, new(RecordId, 1, 0, ReviewDecisionKind.Approve, null, [], "decide-1")));
    }

    [Fact]
    public async Task TheAnalysisAuthorCannotDecideSoAModelPredictionIsNeverAHumanApproval()
    {
        var analysis = new ReviewActor(ReviewAuthor.AnalysisSubject, ReviewRole.Owner);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            new DecideBriefReview(reviews).ExecuteAsync(analysis, new(RecordId, 1, 0, ReviewDecisionKind.Approve, null, [], "decide-1")));
    }

    private static readonly ImmutableArray<BriefParagraph> PersonParagraphs = [new("A person paragraph.", ["R1", "R2", "R3"])];

    private async Task<BriefRevision> PersonTextWithSuggestionAsync()
    {
        _ = await SaveAsync(1, "Person headline", PersonParagraphs, "save-2");
        await ReplaceCitedStatementWithRevisionTwoAsync();
        return await RedraftWithModelTextAsync();
    }

    private async Task ReplaceCitedStatementWithRevisionTwoAsync()
    {
        await seeder.StatementAsync(CitedStatement, 2, "Rent cap amended", "warren");
        await seeder.DecisionAsync(PublishedRecordKind.Statement, CitedStatement, 2, 2, ReviewDecisionKind.Approve);
        await seeder.ReplaceActiveReleaseAsync(2, InWeek.AddHours(1), Binding(PublishedRecordKind.Statement, CitedStatement, 2),
            Binding(PublishedRecordKind.Action, CitedAction, 1), Binding(PublishedRecordKind.DocumentChange, CitedDocumentChange, 1));
    }

    private async Task<BriefRevision> RedraftWithModelTextAsync()
    {
        _ = await drafts.RedraftAsync(Week, default);
        var model = new FakeBriefDraftModel().Draft(Output("Model headline", paragraphs: [("A model paragraph.", ["R1", "R2", "R3"])]));
        await AnalysisWorkers.BriefDrafting(drafts, new PostgresAnalysisStatusStore(factory, BriefDraftTask.Task), model,
            new BriefDraftSettings(10_000_000, concurrency: 1), Officials, TimeProvider.System).ExecuteAsync(once: true);
        return (await reviews.GetRevisionAsync(RecordId, 3, default))!;
    }

    private async Task ChangeCitedStatementAsync(string change)
    {
        switch (change)
        {
            case "withdrawn":
                await seeder.DecisionAsync(PublishedRecordKind.Statement, CitedStatement, 1, 2, ReviewDecisionKind.WithdrawApproval);
                break;
            case "replaced":
                await seeder.StatementAsync(CitedStatement, 2, "Rent cap amended", "warren");
                await seeder.ReplaceActiveReleaseAsync(2, InWeek.AddHours(1), Binding(PublishedRecordKind.Statement, CitedStatement, 2),
                    Binding(PublishedRecordKind.Action, CitedAction, 1), Binding(PublishedRecordKind.DocumentChange, CitedDocumentChange, 1));
                break;
            default:
                await seeder.ReplaceActiveReleaseAsync(2, InWeek.AddHours(1), Binding(PublishedRecordKind.Action, CitedAction, 1),
                    Binding(PublishedRecordKind.DocumentChange, CitedDocumentChange, 1));
                break;
        }
    }

    private Task<BriefRevision> SaveAsync(int expected, string headline, IEnumerable<BriefParagraph> paragraphs, string key,
        SuggestionChoice suggestion = SuggestionChoice.Keep) =>
        new SaveBriefDraft(reviews).ExecuteAsync(Reviewer, new(RecordId, expected, headline, [.. paragraphs], suggestion, key));

    private Task<ReviewDecision> DecideAsync(int revision, int stateVersion, ReviewDecisionKind kind, string? note = null) =>
        new DecideBriefReview(reviews).ExecuteAsync(Reviewer, new(RecordId, revision, stateVersion, kind, note, [], Guid.NewGuid().ToString("N")));

    private async Task<object?> ScalarAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return await command.ExecuteScalarAsync();
    }
}
