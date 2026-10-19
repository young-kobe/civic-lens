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

        await AddSponsorAsync(UpdatedAt.AddDays(1));

        Assert.Equal("AwaitingReview", await ScalarAsync($"SELECT review_status FROM action_drafts WHERE record_id = '{RecordId}'"));
        var needsAction = await new ListActionReviews(ActionReviews()).ExecuteAsync(Reviewer, null, filter: new DraftListFilter(DraftStatusFilter.NeedsAction));
        var item = Assert.Single(needsAction.Items);
        Assert.Equal((2, 1), (item.CurrentRevision.RevisionNumber, item.LastApprovedRevision!.RevisionNumber));
    }

    // The lists filter and sort on these columns, so each new revision must replace them or the lists would show stale sponsors.
    [Fact]
    public async Task EachWorkerRevisionReplacesTheListedColumnsWithThoseOfTheCurrentRevision()
    {
        await DraftAsync(UpdatedAt, DraftableOutput);
        var first = await ListedColumnsAsync();
        Assert.Single(first.Officials);

        await AddSponsorAsync(UpdatedAt.AddDays(1));

        var second = await ListedColumnsAsync();
        Assert.Equal(2, second.Officials.Length);
        Assert.NotEqual(first.CandidateId, second.CandidateId);
        Assert.Equal(second.FromRevision, second.FromColumns);
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

        var results = await BulkApproveAsync(new BulkApprovalTarget(RecordId, 2, 1));

        Assert.Equal([new BulkApprovalResult(RecordId, BulkApprovalOutcome.Approved)], results);
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

        var results = await BulkApproveAsync(new BulkApprovalTarget(RecordId, 2, 2));

        Assert.Equal(BulkApprovalOutcome.Rejected, Assert.Single(results).Outcome);
        Assert.Equal("ChangesRequested", await ScalarAsync($"SELECT review_status FROM action_drafts WHERE record_id = '{RecordId}'"));
    }

    [Fact]
    public async Task ABulkReplayWithTheSameKeyReturnsTheSameResultForEachRecordAndWritesNothingNew()
    {
        await DraftAsync(UpdatedAt, DraftableOutput);
        await DecideAsync(1, 0, ReviewDecisionKind.Approve);
        await AddSponsorAsync(UpdatedAt.AddDays(1));
        var missing = new string('9', 64);
        var request = new BulkApprovalRequest([new(RecordId, 2, 1), new(missing, 1, 0)], "bulk-1");
        var approve = new ApproveSponsorshipChanges(ActionReviews());

        var first = await approve.ExecuteAsync(Reviewer, request);
        var replay = await approve.ExecuteAsync(Reviewer, request);

        Assert.Equal([BulkApprovalOutcome.Approved, BulkApprovalOutcome.NotFound], first.Select(result => result.Outcome));
        Assert.Equal<BulkApprovalResult>(first, replay);
        Assert.Equal(2L, await ScalarAsync("SELECT count(*) FROM action_review_decisions"));
    }

    [Fact]
    public async Task ABulkKeyThatLeavesNoRoomForTheRecordIdIsABadRequestAndNotARejectionOfEachRecord()
    {
        await DraftAsync(UpdatedAt, DraftableOutput);
        var approve = new ApproveSponsorshipChanges(ActionReviews());
        var target = new BulkApprovalTarget(RecordId, 1, 0);

        await Assert.ThrowsAsync<ArgumentException>(() => approve.ExecuteAsync(Reviewer, new([target], new string('k', 192))));
        Assert.Equal(BulkApprovalOutcome.Rejected, Assert.Single(await approve.ExecuteAsync(Reviewer, new([target], new string('k', 191)))).Outcome);
    }

    [Fact]
    public async Task BulkApprovalRejectsARevisionWhoseTextChangedAndReportsEachRecordOnItsOwn()
    {
        await DraftAsync(UpdatedAt, DraftableOutput);
        await DecideAsync(1, 0, ReviewDecisionKind.Approve);
        await SaveAsync(1, SuggestionChoice.Keep, "save-2");
        await AddSponsorAsync(UpdatedAt.AddDays(1));
        var missing = new string('9', 64);

        var results = await BulkApproveAsync(new BulkApprovalTarget(RecordId, 3, 1), new BulkApprovalTarget(missing, 1, 0));

        Assert.Equal([new BulkApprovalResult(RecordId, BulkApprovalOutcome.Rejected), new BulkApprovalResult(missing, BulkApprovalOutcome.NotFound)],
            results);
        Assert.Equal("AwaitingReview", await ScalarAsync($"SELECT review_status FROM action_drafts WHERE record_id = '{RecordId}'"));
        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM action_review_decisions"));
    }

    [Fact]
    public async Task BulkApprovalNeedsAnApprovedRevisionBecauseAFirstDraftHasNoChangeToCompare()
    {
        await DraftAsync(UpdatedAt, DraftableOutput);

        var results = await BulkApproveAsync(new BulkApprovalTarget(RecordId, 1, 0));

        Assert.Equal(BulkApprovalOutcome.Rejected, Assert.Single(results).Outcome);
        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM action_review_decisions"));
    }

    [Fact]
    public async Task TriageListsOnlyActionsThatNeedActionWithTheirExtractionsAndAPassingApprovalCheck()
    {
        await DraftAsync(UpdatedAt, DraftableOutput);
        var triage = new ListActionTriage(ActionReviews());

        var page = await triage.ExecuteAsync(Reviewer, null, new(DraftStatusFilter.Approved));

        var row = Assert.Single(page.Rows);
        Assert.Equal(RecordId, row.Review.RecordId);
        Assert.True(row.Check.Passed);
        Assert.NotEmpty(row.Quotes);
        Assert.All(row.Quotes, quote => Assert.NotEmpty(quote.Text));
        await DecideAsync(1, 0, ReviewDecisionKind.Approve);
        Assert.Empty((await triage.ExecuteAsync(Reviewer, null)).Rows);
    }

    [Fact]
    public async Task ApprovalBindsToTheExactRevisionSoAWorkerRevisionAfterThePageLoadedTurnsItIntoAConflict()
    {
        await DraftAsync(UpdatedAt, DraftableOutput);
        await AddSponsorAsync(UpdatedAt.AddDays(1));

        await Assert.ThrowsAsync<ReviewConflictException>(() => DecideAsync(1, 0, ReviewDecisionKind.Approve));
        Assert.Equal(BulkApprovalOutcome.Conflict, Assert.Single(await BulkApproveAsync(new BulkApprovalTarget(RecordId, 1, 0))).Outcome);
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

    private async Task<(string CandidateId, string[] Officials, (string, string, string, string) FromRevision, (string, string, string, string) FromColumns)>
        ListedColumnsAsync()
    {
        using var revision = System.Text.Json.JsonDocument.Parse((string)(await ScalarAsync($"""
            SELECT r.revision_json FROM action_revisions r JOIN action_drafts d ON d.record_id = r.record_id AND d.current_revision_number = r.revision_number
            WHERE d.record_id = '{RecordId}'
            """))!);
        var root = revision.RootElement;
        var officials = root.GetProperty("sponsorships").EnumerateArray().Select(item => item.GetProperty("personId").GetString()!).Distinct().ToArray();
        var newest = root.GetProperty("sponsorships").EnumerateArray()
            .Select(item => (item.TryGetProperty("withdrawnOn", out var withdrawn) && withdrawn.ValueKind == System.Text.Json.JsonValueKind.String
                ? withdrawn : item.GetProperty("sponsoredOn")).GetString()!).Max()!;
        var candidate = root.GetProperty("candidateId").GetString()!;
        var issues = string.Join(',', root.GetProperty("trackedIssueIds").EnumerateArray().Select(item => item.GetString()));
        var stored = (string)(await ScalarAsync($"""
            SELECT concat_ws('|', candidate_id, event_on::text, array_to_string(issue_ids, ','), array_to_string(official_ids, ','))
            FROM action_drafts WHERE record_id = '{RecordId}'
            """))!;
        var parts = stored.Split('|');
        return (candidate, officials, (candidate, newest, issues, string.Join(',', officials)), (parts[0], parts[1], parts[2], parts[3]));
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

    private async Task<IReadOnlyList<BulkApprovalResult>> BulkApproveAsync(params BulkApprovalTarget[] targets) =>
        await new ApproveSponsorshipChanges(ActionReviews()).ExecuteAsync(Reviewer, new([.. targets], Guid.NewGuid().ToString("N")));

    private Task<ActionDraftRevision> SaveAsync(int expected, SuggestionChoice suggestion, string key,
        string headline = "Person headline", string summary = "Person summary.") =>
        new SaveActionDraft(ActionReviews()).ExecuteAsync(Reviewer,
            new(RecordId, expected, headline, summary, suggestion, key));
}
