using CivicLens.Application.Actions;
using CivicLens.Application.Review;
using CivicLens.Core.Actions;
using CivicLens.Core.Review;
using CivicLens.Infrastructure.Actions;
using CivicLens.Infrastructure.Collection;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Application.Actions;
using CivicLens.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.Collection.BillStream;

public sealed partial class PostgresBillStreamTests
{
    private static readonly ReviewActor Reviewer = new("auth0|reviewer", ReviewRole.Reviewer);

    [Fact]
    public async Task ApprovingAnActionQueuesTheRelationshipPairWithAnApprovedStatementOfItsSponsor()
    {
        await DraftAsync(UpdatedAt, DraftableOutput);
        using var revision = System.Text.Json.JsonDocument.Parse((string)(await ScalarAsync(
            $"SELECT revision_json FROM action_revisions WHERE record_id = '{RecordId}' AND revision_number = 1"))!);
        var bill = revision.RootElement;
        var sponsor = bill.GetProperty("sponsorships")[0].GetProperty("personId").GetString()!;
        var seeder = new RelationshipWorldSeeder(connectionString);
        await seeder.StatementAsync(RelationshipFixtures.StatementAboutBill(sponsor, bill.GetProperty("congress").GetInt32(),
            bill.GetProperty("billNumber").GetInt32()));
        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM relationship_analysis"));

        await DecideAsync(1, 0, ReviewDecisionKind.Approve);

        Assert.Equal(("Pending", "Action", RecordId), ((string)(await ScalarAsync("SELECT status FROM relationship_analysis"))!,
            (string)(await ScalarAsync("SELECT target_kind FROM relationship_analysis"))!,
            (string)(await ScalarAsync("SELECT target_record_id FROM relationship_analysis"))!));
    }

    [Fact]
    public async Task AWorkerRevisionAfterAnApprovalRecomputesTheStoredStatusSoTheRecordNeedsReviewAgain()
    {
        await DraftAsync(UpdatedAt, DraftableOutput);
        await DecideAsync(1, 0, ReviewDecisionKind.Approve);
        Assert.Equal("Approved", await ScalarAsync($"SELECT review_status FROM action_drafts WHERE record_id = '{RecordId}'"));
        var approvedAt = (long)(await ScalarAsync($"SELECT last_revised_at_utc_ticks FROM action_drafts WHERE record_id = '{RecordId}'"))!;

        await AddSponsorAsync(UpdatedAt.AddDays(1));

        Assert.Equal("AwaitingReview", await ScalarAsync($"SELECT review_status FROM action_drafts WHERE record_id = '{RecordId}'"));
        Assert.True((long)(await ScalarAsync($"SELECT last_revised_at_utc_ticks FROM action_drafts WHERE record_id = '{RecordId}'"))! > approvedAt);
        var needsAction = await new ListActionReviews(ActionReviews()).ExecuteAsync(Reviewer, null, filter: DraftStatusFilter.NeedsAction);
        var item = Assert.Single(needsAction.Items);
        Assert.Equal((2, 1), (item.CurrentRevision.RevisionNumber, item.LastApprovedRevision!.RevisionNumber));
    }

    [Fact]
    public async Task ADecisionOnTheRevisionThatTheActiveReleaseServesRequeuesTheBriefOfItsWeek()
    {
        await DraftAsync(UpdatedAt, DraftableOutput);
        var briefs = new BriefWorldSeeder(connectionString);
        await briefs.ReleaseWithoutApprovalsAsync(1, BriefFixtures.InWeek, active: true, BriefWorldSeeder.Binding(PublishedRecordKind.Action, RecordId, 1));
        await briefs.BriefAsync(BriefFixtures.Week);

        await DecideAsync(1, 0, ReviewDecisionKind.Approve);

        Assert.Equal("Pending:0", await briefs.BriefQueueStatusAsync(BriefFixtures.Week));
    }

    [Fact]
    public async Task AWorkerRevisionKeepsAnOpenConcernSoTheNewRevisionStillShowsItsBlocker()
    {
        await DraftAsync(UpdatedAt, DraftableOutput);
        await DecideAsync(1, 0, ReviewDecisionKind.RequestChanges, "Name the bill.");

        await AddSponsorAsync(UpdatedAt.AddDays(1));

        Assert.Equal("AwaitingReviewWithConcerns", await ScalarAsync($"SELECT review_status FROM action_drafts WHERE record_id = '{RecordId}'"));
    }

    [Fact]
    public async Task BulkApprovalApprovesARevisionThatChangesOnlySponsorshipsAndBindsTheDecisionToThatRevision()
    {
        await DraftAsync(UpdatedAt, DraftableOutput);
        await DecideAsync(1, 0, ReviewDecisionKind.Approve);
        await AddSponsorAsync(UpdatedAt.AddDays(1));
        var review = (await new GetActionReview(ActionReviews()).ExecuteAsync(Reviewer, RecordId))!.Review;
        Assert.True(review.IsSponsorshipOnly);
        var added = Assert.Single(review.CurrentRevision.SponsorshipChangesSince(review.LastApprovedRevision!).Added);
        Assert.Equal("B000002", added.BioguideId);

        var results = await BulkApproveAsync(new ActionApprovalTarget(RecordId, 2, 1));

        Assert.Equal([new ActionApprovalResult(RecordId, ActionApprovalOutcome.Approved)], results);
        Assert.Equal("Approved", await ScalarAsync($"SELECT review_status FROM action_drafts WHERE record_id = '{RecordId}'"));
        Assert.Equal(2, await ScalarAsync($"SELECT revision_number FROM action_review_decisions WHERE review_state_version = 2"));
        Assert.Equal(("Bypassed", ActionDraftErrorCodes.SponsorshipsChanged), await TupleAsync(
            "SELECT r.outcome, r.reason FROM action_analysis_runs r JOIN action_revisions v ON v.run_id = r.run_id WHERE v.revision_number = 2"));
    }

    [Fact]
    public async Task BulkApprovalRefusesASponsorshipOnlyRevisionWithAnOpenConcernBecauseItWritesNoResolutionNote()
    {
        await DraftAsync(UpdatedAt, DraftableOutput);
        await DecideAsync(1, 0, ReviewDecisionKind.Approve);
        await AddSponsorAsync(UpdatedAt.AddDays(1));
        await DecideAsync(2, 1, ReviewDecisionKind.RequestChanges, "Check the new sponsor.");
        Assert.True((await new GetActionReview(ActionReviews()).ExecuteAsync(Reviewer, RecordId))!.Review.IsSponsorshipOnly);

        var results = await BulkApproveAsync(new ActionApprovalTarget(RecordId, 2, 2));

        Assert.Equal(ActionApprovalOutcome.Rejected, Assert.Single(results).Outcome);
        Assert.Equal("ChangesRequested", await ScalarAsync($"SELECT review_status FROM action_drafts WHERE record_id = '{RecordId}'"));
    }

    [Fact]
    public async Task ABulkReplayWithTheSameKeyReturnsTheSameResultForEachRecordAndWritesNothingNew()
    {
        await DraftAsync(UpdatedAt, DraftableOutput);
        await DecideAsync(1, 0, ReviewDecisionKind.Approve);
        await AddSponsorAsync(UpdatedAt.AddDays(1));
        var missing = new string('9', 64);
        var request = new ApproveSponsorshipChangesRequest([new(RecordId, 2, 1), new(missing, 1, 0)], "bulk-1");
        var approve = new ApproveSponsorshipChanges(ActionReviews());

        var first = await approve.ExecuteAsync(Reviewer, request);
        var replay = await approve.ExecuteAsync(Reviewer, request);

        Assert.Equal([ActionApprovalOutcome.Approved, ActionApprovalOutcome.NotFound], first.Select(result => result.Outcome));
        Assert.Equal<ActionApprovalResult>(first, replay);
        Assert.Equal(2L, await ScalarAsync("SELECT count(*) FROM action_review_decisions"));
    }

    [Fact]
    public async Task ABulkKeyThatLeavesNoRoomForTheRecordIdIsABadRequestAndNotARejectionOfEachRecord()
    {
        await DraftAsync(UpdatedAt, DraftableOutput);
        var approve = new ApproveSponsorshipChanges(ActionReviews());
        var target = new ActionApprovalTarget(RecordId, 1, 0);

        await Assert.ThrowsAsync<ArgumentException>(() => approve.ExecuteAsync(Reviewer, new([target], new string('k', 192))));
        Assert.Equal(ActionApprovalOutcome.Rejected, Assert.Single(await approve.ExecuteAsync(Reviewer, new([target], new string('k', 191)))).Outcome);
    }

    [Fact]
    public async Task BulkApprovalRejectsARevisionWhoseTextChangedAndReportsEachRecordOnItsOwn()
    {
        await DraftAsync(UpdatedAt, DraftableOutput);
        await DecideAsync(1, 0, ReviewDecisionKind.Approve);
        await SaveAsync(1, SuggestionChoice.Keep, "save-2");
        await AddSponsorAsync(UpdatedAt.AddDays(1));
        var missing = new string('9', 64);

        var results = await BulkApproveAsync(new ActionApprovalTarget(RecordId, 3, 1), new ActionApprovalTarget(missing, 1, 0));

        Assert.Equal([new ActionApprovalResult(RecordId, ActionApprovalOutcome.Rejected), new ActionApprovalResult(missing, ActionApprovalOutcome.NotFound)],
            results);
        Assert.Equal("AwaitingReview", await ScalarAsync($"SELECT review_status FROM action_drafts WHERE record_id = '{RecordId}'"));
        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM action_review_decisions"));
    }

    [Fact]
    public async Task BulkApprovalNeedsAnApprovedRevisionBecauseAFirstDraftHasNoChangeToCompare()
    {
        await DraftAsync(UpdatedAt, DraftableOutput);

        var results = await BulkApproveAsync(new ActionApprovalTarget(RecordId, 1, 0));

        Assert.Equal(ActionApprovalOutcome.Rejected, Assert.Single(results).Outcome);
        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM action_review_decisions"));
    }

    [Fact]
    public async Task ApprovalBindsToTheExactRevisionSoAWorkerRevisionAfterThePageLoadedTurnsItIntoAConflict()
    {
        await DraftAsync(UpdatedAt, DraftableOutput);
        await AddSponsorAsync(UpdatedAt.AddDays(1));

        await Assert.ThrowsAsync<ReviewConflictException>(() => DecideAsync(1, 0, ReviewDecisionKind.Approve));
        Assert.Equal(ActionApprovalOutcome.Conflict, Assert.Single(await BulkApproveAsync(new ActionApprovalTarget(RecordId, 1, 0))).Outcome);
        await DecideAsync(2, 0, ReviewDecisionKind.Approve);

        Assert.Equal(2, await ScalarAsync("SELECT revision_number FROM action_review_decisions"));
    }

    [Fact]
    public async Task APersonSaveMayKeepOrClearTheSuggestionButTheStoreRefusesAChangedOne()
    {
        await DraftAsync(UpdatedAt, DraftableOutput);
        await SaveAsync(1, SuggestionChoice.Keep, "save-2");
        await DraftAsync(UpdatedAt.AddDays(1), DraftableOutput, title: "A renamed bill");
        var withSuggestion = (await ActionReviews().GetRevisionAsync(RecordId, 3, default))!;
        Assert.Equal(("Person headline", ReviewTextOrigin.Person), (withSuggestion.Headline, withSuggestion.TextOrigin));
        Assert.NotNull(withSuggestion.Suggestion);

        var kept = await SaveAsync(3, SuggestionChoice.Keep, "save-4");
        var cleared = await SaveAsync(4, SuggestionChoice.Dismiss, "save-5");

        Assert.Equal(withSuggestion.Suggestion, kept.Suggestion);
        Assert.Null(cleared.Suggestion);
        var forged = cleared with { RevisionNumber = 6, CreatedAtUtc = DateTimeOffset.UtcNow, Suggestion = withSuggestion.Suggestion };
        await Assert.ThrowsAsync<ArgumentException>(() =>
            ActionReviews().SaveRevisionAsync(Reviewer.Subject, forged, 5, "save-6", new string('6', 64), default));
        Assert.Equal(5, await ScalarAsync($"SELECT current_revision_number FROM action_drafts WHERE record_id = '{RecordId}'"));
    }

    [Fact]
    public async Task UsingTheSuggestionSavesItsStoredTextAndClearsItEvenWhenThePostedTextDiffers()
    {
        await DraftAsync(UpdatedAt, DraftableOutput);
        await SaveAsync(1, SuggestionChoice.Keep, "save-2");
        await DraftAsync(UpdatedAt.AddDays(1), DraftableOutput, title: "A renamed bill");
        var suggestion = (await ActionReviews().GetRevisionAsync(RecordId, 3, default))!.Suggestion!;

        var used = await SaveAsync(3, SuggestionChoice.Use, "save-4", headline: "Posted headline", summary: "Posted summary.");

        Assert.Equal((suggestion.Headline, suggestion.Summary, ReviewTextOrigin.Person), (used.Headline, used.Summary, used.TextOrigin));
        Assert.Null(used.Suggestion);
    }

    [Fact]
    public async Task UsingASuggestionThatTheRevisionDoesNotHaveIsRefused()
    {
        await DraftAsync(UpdatedAt, DraftableOutput);

        await Assert.ThrowsAsync<ArgumentException>(() => SaveAsync(1, SuggestionChoice.Use, "save-2"));
    }

    [Fact]
    public async Task TheDatabaseRefusesAnActionStatusThatThePolicyNeverStores()
    {
        await DraftAsync(UpdatedAt, DraftableOutput);

        await Assert.ThrowsAsync<PostgresException>(() =>
            ExecuteAsync(connectionString, $"UPDATE action_drafts SET review_status = 'NotDrafted' WHERE record_id = '{RecordId}'"));
    }

    private PostgresActionReviewStore ActionReviews() => new(new PooledDbContextFactory<CollectionAttemptDbContext>(
        new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options));

    private async Task DraftAsync(DateTimeOffset updatedAt, string output, string? title = null)
    {
        _ = await ReadableCandidateAsync("A000001", updatedAt, title: title);
        Assert.Single((await RunActionWorkerAsync(new FakeHeadlineSummaryModel().Draft(output))).Requests);
    }

    private async Task AddSponsorAsync(DateTimeOffset updatedAt)
    {
        _ = await ReadableCandidateAsync("A000001", updatedAt, cosponsor: "B000002");
        Assert.Empty((await RunActionWorkerAsync(new FakeHeadlineSummaryModel())).Requests);
    }

    private Task<ReviewDecision> DecideAsync(int revision, int stateVersion, ReviewDecisionKind kind, string? note = null) =>
        new DecideActionReview(ActionReviews()).ExecuteAsync(Reviewer,
            new(RecordId, revision, stateVersion, kind, note, [], Guid.NewGuid().ToString("N")));

    private async Task<IReadOnlyList<ActionApprovalResult>> BulkApproveAsync(params ActionApprovalTarget[] targets) =>
        await new ApproveSponsorshipChanges(ActionReviews()).ExecuteAsync(Reviewer, new([.. targets], Guid.NewGuid().ToString("N")));

    private Task<ActionDraftRevision> SaveAsync(int expected, SuggestionChoice suggestion, string key,
        string headline = "Person headline", string summary = "Person summary.") =>
        new SaveActionDraft(ActionReviews()).ExecuteAsync(Reviewer,
            new(RecordId, expected, headline, summary, suggestion, key));
}
