using CivicLens.Application.Review;
using CivicLens.Application.Votes;
using CivicLens.Core.Review;
using CivicLens.Core.Votes;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Votes;
using CivicLens.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace CivicLens.Tests.Infrastructure.Collection.HouseVotes;

public sealed partial class PostgresHouseVoteTests
{
    private static readonly ReviewActor Reviewer = new("auth0|reviewer", ReviewRole.Reviewer);

    [Fact]
    public async Task ApprovingAVoteQueuesTheRelationshipPairWithAnApprovedStatementOfAMember()
    {
        var candidate = await PendingCandidateAsync();
        await SettleForReviewAsync(candidate, await RevisionAsync(candidate, 1));
        var recordId = await RecordIdAsync(candidate);
        using var revision = System.Text.Json.JsonDocument.Parse((string)(await ScalarAsync(
            $"SELECT revision_json FROM vote_revisions WHERE record_id = '{recordId}' AND revision_number = 1"))!);
        var vote = revision.RootElement;
        var member = vote.GetProperty("members")[0].GetProperty("personId").GetString()!;
        await new RelationshipWorldSeeder(connectionString).StatementAsync(RelationshipFixtures.StatementAboutBill(member,
            vote.GetProperty("congress").GetInt32(), vote.GetProperty("billNumber").GetInt32(), "H.Con.Res."));
        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM relationship_analysis"));

        await DecideAsync(recordId, 1, 0, ReviewDecisionKind.Approve);

        Assert.Equal(("Pending", "Vote", recordId), ((string)(await ScalarAsync("SELECT status FROM relationship_analysis"))!,
            (string)(await ScalarAsync("SELECT target_kind FROM relationship_analysis"))!,
            (string)(await ScalarAsync("SELECT target_record_id FROM relationship_analysis"))!));
    }

    [Fact]
    public async Task ApprovingAVotePairsOnlyDatedStatementsOfItsCongressBecauseAVotePairsByNumberAndANumberNeedsTheDate()
    {
        var candidate = await PendingCandidateAsync();
        await SettleForReviewAsync(candidate, await RevisionAsync(candidate, 1));
        var recordId = await RecordIdAsync(candidate);
        using var revision = System.Text.Json.JsonDocument.Parse((string)(await ScalarAsync(
            $"SELECT revision_json FROM vote_revisions WHERE record_id = '{recordId}' AND revision_number = 1"))!);
        var vote = revision.RootElement;
        var (member, congress, number) = (vote.GetProperty("members")[0].GetProperty("personId").GetString()!,
            vote.GetProperty("congress").GetInt32(), vote.GetProperty("billNumber").GetInt32());
        var seeder = new RelationshipWorldSeeder(connectionString);
        await seeder.StatementAsync(RelationshipFixtures.StatementAboutBill(member, congress, number, "H.Con.Res.", new string('1', 64)));
        await seeder.StatementAsync(RelationshipFixtures.StatementAboutBill(member, congress - 1, number, "H.Con.Res.", new string('2', 64)));
        await seeder.StatementAsync(RelationshipFixtures.StatementAboutBill(member, congress, number, "H.Con.Res.", new string('3', 64), dated: false));

        await DecideAsync(recordId, 1, 0, ReviewDecisionKind.Approve);

        Assert.Equal([[new string('1', 64)]], await seeder.ReadAsync("SELECT statement_candidate_id FROM relationship_analysis"));
    }

    [Fact]
    public async Task CodeApprovesAWorkerVoteRevisionInTheSameTransactionAndTheRecordIsReadyToPublish()
    {
        var candidate = await PendingCandidateAsync();

        await SettleAsync(candidate, await RevisionAsync(candidate, 1));

        var recordId = await RecordIdAsync(candidate);
        var decision = Assert.Single(await VoteDecisionsAsync());
        Assert.Equal((ReviewDecisionKind.Approve, ReviewAuthor.CodeSubject, 1, 1), (decision.Kind, decision.ActorSubject, decision.RevisionNumber,
            decision.ReviewStateVersion));
        Assert.Equal("Approved", await ScalarAsync($"SELECT review_status FROM vote_drafts WHERE record_id = '{recordId}'"));
        Assert.Equal(recordId, Assert.Single(await VoteReviews().ListUnpublishedApprovedAsync(10, [], default)).Vote.RecordId);
    }

    [Fact]
    public async Task CodeApprovesAWorkerRevisionAfterAnEarlierCodeApprovalAndCountsItAsApproved()
    {
        var first = await PendingCandidateAsync();
        await SettleAsync(first, await RevisionAsync(first, 1));
        var recordId = await RecordIdAsync(first);

        var second = await PendingCandidateAsync(Updated.AddDays(1));
        await SettleAsync(second, await RevisionAsync(second, 2));

        Assert.Equal([(1, ReviewAuthor.CodeSubject), (2, ReviewAuthor.CodeSubject)],
            (await VoteDecisionsAsync()).Select(decision => (decision.RevisionNumber, decision.ActorSubject)));
        Assert.Equal("Approved", await ScalarAsync("SELECT review_status FROM vote_drafts"));
        Assert.Equal(await ScalarAsync("SELECT max(created_at_utc_ticks) FROM vote_revisions"),
            await ScalarAsync("SELECT last_revised_at_utc_ticks FROM vote_drafts"));
    }

    [Fact]
    public async Task ACodeApprovalOfAVoteRunsTheRelationshipHookSoAnApprovedStatementOfAMemberQueuesItsPair()
    {
        var first = await PendingCandidateAsync();
        await SettleAsync(first, await RevisionAsync(first, 1));
        var recordId = await RecordIdAsync(first);
        using var revision = System.Text.Json.JsonDocument.Parse((string)(await ScalarAsync(
            $"SELECT revision_json FROM vote_revisions WHERE record_id = '{recordId}' AND revision_number = 1"))!);
        var vote = revision.RootElement;
        var member = vote.GetProperty("members")[0].GetProperty("personId").GetString()!;
        await new RelationshipWorldSeeder(connectionString).StatementAsync(RelationshipFixtures.StatementAboutBill(member,
            vote.GetProperty("congress").GetInt32(), vote.GetProperty("billNumber").GetInt32(), "H.Con.Res."));
        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM relationship_analysis"));

        var second = await PendingCandidateAsync(Updated.AddDays(1));
        await SettleAsync(second, await RevisionAsync(second, 2));

        Assert.Equal(("Pending", "Vote", recordId), ((string)(await ScalarAsync("SELECT status FROM relationship_analysis"))!,
            (string)(await ScalarAsync("SELECT target_kind FROM relationship_analysis"))!,
            (string)(await ScalarAsync("SELECT target_record_id FROM relationship_analysis"))!));
    }

    [Fact]
    public async Task ACodeApprovalLeavesAVoteForAPersonWhenAnApprovalCheckFails()
    {
        var candidate = await PendingCandidateAsync();
        var revision = await RevisionAsync(candidate, 1);

        await SettleAsync(candidate, revision with { Facts = [new("Result: Passed", new(new string('f', 64), 0, 5))] });

        Assert.Empty(await VoteDecisionsAsync());
        Assert.Equal("AwaitingReview", await ScalarAsync("SELECT review_status FROM vote_drafts"));
        Assert.Equal("A vote citation names a missing extraction.", await ScalarAsync("SELECT held_reason FROM vote_drafts"));
    }

    [Fact]
    public async Task AWorkerRevisionKeepsAnOpenConcernSoCodeDoesNotApproveIt()
    {
        var first = await PendingCandidateAsync();
        await SettleForReviewAsync(first, await RevisionAsync(first, 1));
        var recordId = await RecordIdAsync(first);
        await DecideAsync(recordId, 1, 0, ReviewDecisionKind.RequestChanges, "Name the bill.");

        var second = await PendingCandidateAsync(Updated.AddDays(1));
        await SettleAsync(second, await RevisionAsync(second, 2));

        Assert.Equal("AwaitingReviewWithConcerns", await ScalarAsync($"SELECT review_status FROM vote_drafts WHERE record_id = '{recordId}'"));
        Assert.Equal(ReviewDecisionKind.RequestChanges, Assert.Single(await VoteDecisionsAsync()).Kind);
    }

    [Fact]
    public async Task APersonEditAfterACodeApprovalWaitsForAPersonAndAWorkerRevisionOverItStaysForAPerson()
    {
        var first = await PendingCandidateAsync();
        await SettleAsync(first, await RevisionAsync(first, 1));
        var recordId = await RecordIdAsync(first);
        await SaveAsync(recordId, 1, SuggestionChoice.Keep, "save-2");
        Assert.Equal("AwaitingReview", await ScalarAsync($"SELECT review_status FROM vote_drafts WHERE record_id = '{recordId}'"));

        var second = await PendingCandidateAsync(Updated.AddDays(1));
        await SettleAsync(second, (await RevisionAsync(second, 3)) with { TextOrigin = ReviewTextOrigin.Person, Headline = "Person headline", Summary = "Person summary." });

        Assert.Single(await VoteDecisionsAsync());
        Assert.Equal("AwaitingReview", await ScalarAsync($"SELECT review_status FROM vote_drafts WHERE record_id = '{recordId}'"));
    }

    // The lists filter and sort on these columns, so each new revision must replace them or the lists would show stale members.
    [Fact]
    public async Task EachWorkerRevisionReplacesTheListedColumnsWithThoseOfTheCurrentRevision()
    {
        var first = await PendingCandidateAsync();
        await SettleForReviewAsync(first, await RevisionAsync(first, 1));
        var recordId = await RecordIdAsync(first);
        var second = await PendingCandidateAsync(Updated.AddDays(1));
        var revised = await RevisionAsync(second, 2);
        await SettleForReviewAsync(second, revised with { VotedOn = revised.VotedOn.AddDays(3), TrackedIssueIds = ["housing"], IssueCandidateIds = ["housing"] });

        var stored = (string)(await ScalarAsync($"""
            SELECT concat_ws('|', candidate_id, event_on::text, array_to_string(issue_ids, ','), array_to_string(official_ids, ','))
            FROM vote_drafts WHERE record_id = '{recordId}'
            """))!;

        var expected = revised with { VotedOn = revised.VotedOn.AddDays(3) };
        Assert.Equal($"{second}|{expected.VotedOn:yyyy-MM-dd}|housing|{string.Join(',', expected.OfficialIds())}", stored);
    }

    [Fact]
    public async Task ApprovalBindsToTheExactRevisionSoAWorkerRevisionAfterThePageLoadedTurnsItIntoAConflict()
    {
        var first = await PendingCandidateAsync();
        await SettleForReviewAsync(first, await RevisionAsync(first, 1));
        var recordId = await RecordIdAsync(first);
        var second = await PendingCandidateAsync(Updated.AddDays(1));
        await SettleForReviewAsync(second, await RevisionAsync(second, 2));

        await Assert.ThrowsAsync<ReviewConflictException>(() => DecideAsync(recordId, 1, 0, ReviewDecisionKind.Approve));
        await DecideAsync(recordId, 2, 0, ReviewDecisionKind.Approve);

        Assert.Equal(2, await ScalarAsync("SELECT revision_number FROM vote_review_decisions"));
    }

    [Fact]
    public async Task ADecisionWithAStaleReviewStateVersionIsAConflictSoTwoReviewersCannotOverwriteEachOther()
    {
        var candidate = await PendingCandidateAsync();
        await SettleForReviewAsync(candidate, await RevisionAsync(candidate, 1));
        var recordId = await RecordIdAsync(candidate);
        await DecideAsync(recordId, 1, 0, ReviewDecisionKind.Approve);

        await Assert.ThrowsAsync<ReviewConflictException>(() => DecideAsync(recordId, 1, 0, ReviewDecisionKind.RequestChanges, "Check the kind."));

        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM vote_review_decisions"));
    }

    [Fact]
    public async Task ApprovalChecksEveryCitationAgainstItsSavedExtractionSoNoApprovalRestsOnAMissingLine()
    {
        var candidate = await PendingCandidateAsync();
        var revision = await RevisionAsync(candidate, 1);
        await SettleForReviewAsync(candidate, revision with { Facts = [new("Result: Passed", new(new string('c', 64), 0, 5))] });
        var recordId = await RecordIdAsync(candidate);

        await Assert.ThrowsAsync<ArgumentException>(() => DecideAsync(recordId, 1, 0, ReviewDecisionKind.Approve));

        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM vote_review_decisions"));
    }

    [Fact]
    public async Task TheReviewReadHoldsTheCitedExtractionsAndTheConfiguredNamesOfTheTrackedMembersSoThePageCanShowEachCast()
    {
        var candidate = await PendingCandidateAsync();
        await SettleForReviewAsync(candidate, await RevisionAsync(candidate, 1));
        var recordId = await RecordIdAsync(candidate);

        var read = (await new GetVoteReview(VoteReviews()).ExecuteAsync(Reviewer, recordId))!;

        Assert.Equal("A", read.PersonNames["with-id-a"]);
        Assert.Equal("Yea", Assert.Single(read.Review.CurrentRevision.Members).Cast);
        Assert.All(read.Review.CurrentRevision.Citations, span => Assert.Contains(span.ExtractionId, read.Extractions.Keys));
        Assert.Null(await new GetVoteReview(VoteReviews()).ExecuteAsync(Reviewer, new string('9', 64)));
    }

    [Fact]
    public async Task TheReviewReadFailsLoudWhenARevisionNamesACandidateThatIsMissingSoNoCastShowsWithoutItsNames()
    {
        var candidate = await PendingCandidateAsync();
        await SettleForReviewAsync(candidate, await RevisionAsync(candidate, 1));
        var recordId = await RecordIdAsync(candidate);
        await ExecuteAsync($"SET session_replication_role = replica; DELETE FROM vote_candidates WHERE candidate_id = '{candidate}'");

        await Assert.ThrowsAsync<InvalidDataException>(() => new GetVoteReview(VoteReviews()).ExecuteAsync(Reviewer, recordId));
    }

    [Fact]
    public async Task APersonSaveMayKeepOrClearTheSuggestionButTheStoreRefusesAChangedOne()
    {
        var recordId = await RecordWithPersonTextAndSuggestionAsync();
        var withSuggestion = (await VoteReviews().GetRevisionAsync(recordId, 3, default))!;
        Assert.Equal(("Person headline", ReviewTextOrigin.Person), (withSuggestion.Headline, withSuggestion.TextOrigin));
        Assert.NotNull(withSuggestion.Suggestion);

        var kept = await SaveAsync(recordId, 3, SuggestionChoice.Keep, "save-4");
        var cleared = await SaveAsync(recordId, 4, SuggestionChoice.Dismiss, "save-5");

        Assert.Equal(withSuggestion.Suggestion, kept.Suggestion);
        Assert.Null(cleared.Suggestion);
        var forged = cleared with { RevisionNumber = 6, CreatedAtUtc = DateTimeOffset.UtcNow, Suggestion = withSuggestion.Suggestion with { Headline = "Forged" } };
        await Assert.ThrowsAsync<ArgumentException>(() =>
            VoteReviews().SaveRevisionAsync(Reviewer.Subject, forged, 5, "save-6", new string('6', 64), default));
        Assert.Equal(5, await ScalarAsync($"SELECT current_revision_number FROM vote_drafts WHERE record_id = '{recordId}'"));
    }

    [Fact]
    public async Task UsingTheSuggestionSavesItsStoredTextAndClearsItEvenWhenThePostedTextDiffers()
    {
        var recordId = await RecordWithPersonTextAndSuggestionAsync();
        var suggestion = (await VoteReviews().GetRevisionAsync(recordId, 3, default))!.Suggestion!;

        var used = await SaveAsync(recordId, 3, SuggestionChoice.Use, "save-4", headline: "Posted headline", summary: "Posted summary.");

        Assert.Equal((suggestion.Headline, suggestion.Summary, ReviewTextOrigin.Person), (used.Headline, used.Summary, used.TextOrigin));
        Assert.Null(used.Suggestion);
    }

    [Fact]
    public async Task ARevisionStoredBeforeTheModelChoiceHasNoCandidateSetAndStillPublishesOpensAndTakesAPersonEdit()
    {
        var recordId = await ApprovedVoteAsync();
        await ExecuteAsync("UPDATE vote_revisions SET revision_json = (revision_json::jsonb - 'issueCandidateIds')::text");
        var reviews = VoteReviews();

        var unpublished = Assert.Single(await reviews.ListUnpublishedApprovedAsync(10, [], default));
        var publishable = await reviews.GetForPublicationAsync([recordId], default);
        var read = await reviews.GetRevisionAsync(recordId, 1, default);
        var edited = await SaveAsync(recordId, 1, SuggestionChoice.Keep, "save-legacy");

        Assert.True(unpublished.Vote.CurrentRevision.IssueCandidateIds.IsDefault);
        Assert.Equal(recordId, Assert.Single(publishable).Key);
        Assert.True(read!.IssueCandidateIds.IsDefault);
        Assert.Equal((2, ReviewTextOrigin.Person), (edited.RevisionNumber, edited.TextOrigin));
    }

    [Fact]
    public async Task UsingASuggestionThatTheRevisionDoesNotHaveIsRefused()
    {
        var candidate = await PendingCandidateAsync();
        await SettleForReviewAsync(candidate, await RevisionAsync(candidate, 1));

        var recordId = await RecordIdAsync(candidate);

        await Assert.ThrowsAsync<ArgumentException>(() => SaveAsync(recordId, 1, SuggestionChoice.Use, "save-2"));
    }

    private async Task<string> RecordWithPersonTextAndSuggestionAsync()
    {
        var first = await PendingCandidateAsync();
        await SettleForReviewAsync(first, await RevisionAsync(first, 1));
        var recordId = await RecordIdAsync(first);
        var edited = await SaveAsync(recordId, 1, SuggestionChoice.Keep, "save-2");
        var second = await PendingCandidateAsync(Updated.AddDays(1));
        var text = ReviewText.AfterModelDraft(edited, "Model headline", "Model summary.", new string('1', 32), new string('d', 64));
        await SettleForReviewAsync(second, (await RevisionAsync(second, 3)) with
        {
            Headline = text.Headline,
            Summary = text.Summary,
            TextOrigin = text.Origin,
            Suggestion = text.Suggestion
        });
        return recordId;
    }

    private async Task<IReadOnlyList<ReviewDecision>> VoteDecisionsAsync()
    {
        var json = (string)(await ScalarAsync("SELECT coalesce(jsonb_agg(decision_json::jsonb ORDER BY review_state_version), '[]')::text FROM vote_review_decisions"))!;
        return System.Text.Json.JsonSerializer.Deserialize<ReviewDecision[]>(json, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
    }

    private PostgresVoteReviewStore VoteReviews() => new(new PooledDbContextFactory<CollectionAttemptDbContext>(
        new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options));

    private Task<ReviewDecision> DecideAsync(string recordId, int revision, int stateVersion, ReviewDecisionKind kind, string? note = null) =>
        new DecideVoteReview(VoteReviews()).ExecuteAsync(Reviewer,
            new(recordId, revision, stateVersion, kind, note, [], Guid.NewGuid().ToString("N")));

    private Task<VoteDraftRevision> SaveAsync(string recordId, int expected, SuggestionChoice suggestion, string key,
        string headline = "Person headline", string summary = "Person summary.") =>
        new SaveVoteDraft(VoteReviews()).ExecuteAsync(Reviewer, new(recordId, expected, headline, summary, suggestion, key));
}
