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
        await DraftForReviewAsync(UpdatedAt, DraftableOutput);
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
    public async Task CodeApprovesAWorkerRevisionInTheSameTransactionAndTheRecordIsReadyToPublish()
    {
        await DraftAsync(UpdatedAt, DraftableOutput);

        var decision = Assert.Single(await ActionDecisionsAsync());
        Assert.Equal((ReviewDecisionKind.Approve, ReviewAuthor.CodeSubject, 1, 1), (decision.Kind, decision.ActorSubject, decision.RevisionNumber,
            decision.ReviewStateVersion));
        Assert.Equal("Approved", await ScalarAsync($"SELECT review_status FROM action_drafts WHERE record_id = '{RecordId}'"));
        Assert.Equal(RecordId, Assert.Single(await ActionReviews().ListUnpublishedApprovedAsync(10, [], default)).Action.RecordId);
    }

    [Fact]
    public async Task CodeApprovesAWorkerRevisionThatOnlyCopiesTheTextAfterANewSponsorJoins()
    {
        await DraftAsync(UpdatedAt, DraftableOutput);

        await AddSponsorAsync(UpdatedAt.AddDays(1));

        Assert.Equal([(1, ReviewAuthor.CodeSubject), (2, ReviewAuthor.CodeSubject)],
            (await ActionDecisionsAsync()).Select(decision => (decision.RevisionNumber, decision.ActorSubject)));
        Assert.Equal("Approved", await ScalarAsync($"SELECT review_status FROM action_drafts WHERE record_id = '{RecordId}'"));
    }

    [Fact]
    public async Task ACodeApprovalRunsTheRelationshipHookSoAnApprovedStatementOfTheSponsorQueuesItsPair()
    {
        await DraftAsync(UpdatedAt, DraftableOutput);
        using var revision = System.Text.Json.JsonDocument.Parse((string)(await ScalarAsync(
            $"SELECT revision_json FROM action_revisions WHERE record_id = '{RecordId}' AND revision_number = 1"))!);
        var bill = revision.RootElement;
        var sponsor = bill.GetProperty("sponsorships")[0].GetProperty("personId").GetString()!;
        await new RelationshipWorldSeeder(connectionString).StatementAsync(RelationshipFixtures.StatementAboutBill(sponsor,
            bill.GetProperty("congress").GetInt32(), bill.GetProperty("billNumber").GetInt32()));
        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM relationship_analysis"));

        await AddSponsorAsync(UpdatedAt.AddDays(1));

        Assert.Equal(("Pending", "Action", RecordId), ((string)(await ScalarAsync("SELECT status FROM relationship_analysis"))!,
            (string)(await ScalarAsync("SELECT target_kind FROM relationship_analysis"))!,
            (string)(await ScalarAsync("SELECT target_record_id FROM relationship_analysis"))!));
    }

    [Fact]
    public async Task AFailingHookRollsBackTheRevisionAndTheCodeDecisionTogether()
    {
        await DraftAsync(UpdatedAt, DraftableOutput);
        using var revision = System.Text.Json.JsonDocument.Parse((string)(await ScalarAsync(
            $"SELECT revision_json FROM action_revisions WHERE record_id = '{RecordId}' AND revision_number = 1"))!);
        var bill = revision.RootElement;
        await new RelationshipWorldSeeder(connectionString).StatementAsync(RelationshipFixtures.StatementAboutBill(
            bill.GetProperty("sponsorships")[0].GetProperty("personId").GetString()!, bill.GetProperty("congress").GetInt32(),
            bill.GetProperty("billNumber").GetInt32()));
        await ExecuteAsync(connectionString, """
            CREATE FUNCTION fail_pair_insert() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'hook failed'; END $$;
            CREATE TRIGGER fail_pair_insert BEFORE INSERT ON relationship_analysis FOR EACH ROW EXECUTE FUNCTION fail_pair_insert()
            """);
        _ = await ReadableCandidateAsync("A000001", UpdatedAt.AddDays(1), cosponsor: "B000002");

        await RunActionWorkerAsync(new FakeHeadlineSummaryModel());

        Assert.Equal((1, 1L, 1L), (await ScalarAsync($"SELECT current_revision_number FROM action_drafts WHERE record_id = '{RecordId}'"),
            await ScalarAsync("SELECT count(*) FROM action_revisions"), await ScalarAsync("SELECT count(*) FROM action_review_decisions")));
        await ExecuteAsync(connectionString, "DROP TRIGGER fail_pair_insert ON relationship_analysis; UPDATE action_analysis SET status = 'Pending', retry_at = NULL");
        await RunActionWorkerAsync(new FakeHeadlineSummaryModel());
        Assert.Equal((2, 2L), (await ScalarAsync($"SELECT current_revision_number FROM action_drafts WHERE record_id = '{RecordId}'"),
            await ScalarAsync("SELECT count(*) FROM action_review_decisions")));
    }

    [Fact]
    public async Task ACodeApprovalOfTheRevisionThatTheActiveReleaseServesRequeuesTheBriefOfItsWeek()
    {
        var briefs = new BriefWorldSeeder(connectionString);
        await briefs.ReleaseWithoutApprovalsAsync(1, BriefFixtures.InWeek, active: true, BriefWorldSeeder.Binding(PublishedRecordKind.Action, RecordId, 1));
        await briefs.BriefAsync(BriefFixtures.Week);

        await DraftAsync(UpdatedAt, DraftableOutput);

        Assert.Equal("Pending:0", await briefs.BriefQueueStatusAsync(BriefFixtures.Week));
    }

    [Fact]
    public async Task ACodeApprovalLeavesTheRecordForAPersonWhenAnApprovalCheckFails()
    {
        var candidate = await CandidateWithoutSummariesAsync();

        await SettleAsync(candidate, Revision(candidate, 1));

        Assert.Empty(await ActionDecisionsAsync());
        var listed = Assert.Single((await new ListActionsNeedingAction(ActionReviews()).ExecuteAsync(Reviewer)).Items);
        Assert.Equal(ReviewStatus.AwaitingReview, listed.CurrentStatus);
    }

    [Fact]
    public async Task APersonEditAfterACodeApprovalWaitsForAPersonAndKeepsTheCodeApprovedRevisionAsTheLastApprovedOne()
    {
        await DraftAsync(UpdatedAt, DraftableOutput);

        await SaveAsync(1, SuggestionChoice.Keep, "save-2");

        Assert.Single(await ActionDecisionsAsync());
        Assert.Equal("AwaitingReview", await ScalarAsync($"SELECT review_status FROM action_drafts WHERE record_id = '{RecordId}'"));
        var needsAction = await new ListActionsNeedingAction(ActionReviews()).ExecuteAsync(Reviewer);
        var item = Assert.Single(needsAction.Items);
        Assert.Equal((2, 1), (item.CurrentRevision.RevisionNumber, item.LastApprovedRevision!.RevisionNumber));
        Assert.Equal(await ScalarAsync("SELECT max(created_at_utc_ticks) FROM action_revisions"),
            await ScalarAsync("SELECT last_revised_at_utc_ticks FROM action_drafts"));
    }

    [Fact]
    public async Task AWorkerRevisionWithTextAPersonWroteStaysForAPersonWhenNoPersonApprovedThatText()
    {
        await DraftAsync(UpdatedAt, DraftableOutput);
        await SaveAsync(1, SuggestionChoice.Keep, "save-2");

        await AddSponsorAsync(UpdatedAt.AddDays(1));

        Assert.Single(await ActionDecisionsAsync());
        Assert.Equal("AwaitingReview", await ScalarAsync($"SELECT review_status FROM action_drafts WHERE record_id = '{RecordId}'"));
    }

    [Fact]
    public async Task AWorkerRedraftAfterAPersonApprovedAPersonEditStaysAwaitingReviewWithTheModelSuggestionAttached()
    {
        await DraftAsync(UpdatedAt, DraftableOutput);
        await SaveAsync(1, SuggestionChoice.Keep, "save-2");
        await DecideAsync(2, 1, ReviewDecisionKind.Approve);

        await DraftAsync(UpdatedAt.AddDays(1), DraftableOutput, title: "A renamed bill");

        var redraft = (await ActionReviews().GetRevisionAsync(RecordId, 3, default))!;
        Assert.Equal((ReviewTextOrigin.Person, true), (redraft.TextOrigin, redraft.Suggestion is not null));
        Assert.Equal("AwaitingReview", await ScalarAsync($"SELECT review_status FROM action_drafts WHERE record_id = '{RecordId}'"));
        Assert.Equal([ReviewAuthor.CodeSubject, Reviewer.Subject], (await ActionDecisionsAsync()).Select(decision => decision.ActorSubject));
    }

    [Fact]
    public async Task AWorkerRevisionKeepsAnOpenConcernSoCodeDoesNotApproveItAndTheBlockerStaysVisible()
    {
        await DraftForReviewAsync(UpdatedAt, DraftableOutput);
        await DecideAsync(1, 0, ReviewDecisionKind.RequestChanges, "Name the bill.");

        await AddSponsorAsync(UpdatedAt.AddDays(1));

        Assert.Equal("AwaitingReviewWithConcerns", await ScalarAsync($"SELECT review_status FROM action_drafts WHERE record_id = '{RecordId}'"));
        Assert.Equal(ReviewDecisionKind.RequestChanges, Assert.Single(await ActionDecisionsAsync()).Kind);
    }

    [Fact]
    public async Task APersonWithdrawalStaysEffectiveWhenTheWorkerDraftsAgain()
    {
        await DraftAsync(UpdatedAt, DraftableOutput);
        await DecideAsync(1, 1, ReviewDecisionKind.WithdrawApproval, "The sponsor list is wrong.");

        await AddSponsorAsync(UpdatedAt.AddDays(1));

        Assert.Equal("AwaitingReviewWithConcerns", await ScalarAsync($"SELECT review_status FROM action_drafts WHERE record_id = '{RecordId}'"));
        Assert.Equal([ReviewDecisionKind.Approve, ReviewDecisionKind.WithdrawApproval], (await ActionDecisionsAsync()).Select(decision => decision.Kind));
    }

    [Fact]
    public async Task APersonCannotActAsTheCodeSubject()
    {
        await DraftForReviewAsync(UpdatedAt, DraftableOutput);
        var impostor = new ReviewActor(ReviewAuthor.CodeSubject, ReviewRole.Owner);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new DecideActionReview(ActionReviews()).ExecuteAsync(impostor,
            new(RecordId, 1, 0, ReviewDecisionKind.Approve, null, [], Guid.NewGuid().ToString("N"))));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => ActionReviews().DecideAsync(ReviewAuthor.CodeSubject,
            new(Guid.NewGuid().ToString("N"), RecordId, 1, 1, ReviewDecisionKind.Approve, ReviewAuthor.CodeSubject, null, [], DateTimeOffset.UtcNow),
            1, 0, "key", new string('a', 64), default));
        Assert.Empty(await ActionDecisionsAsync());
    }

    // The lists filter and sort on these columns, so each new revision must replace them or the lists would show stale sponsors.
    [Fact]
    public async Task EachWorkerRevisionReplacesTheListedColumnsWithThoseOfTheCurrentRevision()
    {
        await DraftForReviewAsync(UpdatedAt, DraftableOutput);
        var first = await ListedColumnsAsync();
        Assert.Single(first.Officials);

        await AddSponsorForReviewAsync(UpdatedAt.AddDays(1));

        var second = await ListedColumnsAsync();
        Assert.Equal(2, second.Officials.Length);
        Assert.NotEqual(first.CandidateId, second.CandidateId);
        Assert.Equal(second.FromRevision, second.FromColumns);
    }

    [Fact]
    public async Task ADecisionOnTheRevisionThatTheActiveReleaseServesRequeuesTheBriefOfItsWeek()
    {
        await DraftForReviewAsync(UpdatedAt, DraftableOutput);
        var briefs = new BriefWorldSeeder(connectionString);
        await briefs.ReleaseWithoutApprovalsAsync(1, BriefFixtures.InWeek, active: true, BriefWorldSeeder.Binding(PublishedRecordKind.Action, RecordId, 1));
        await briefs.BriefAsync(BriefFixtures.Week);

        await DecideAsync(1, 0, ReviewDecisionKind.Approve);

        Assert.Equal("Pending:0", await briefs.BriefQueueStatusAsync(BriefFixtures.Week));
    }

    [Fact]
    public async Task ApprovalBindsToTheExactRevisionSoAWorkerRevisionAfterThePageLoadedTurnsItIntoAConflict()
    {
        await DraftForReviewAsync(UpdatedAt, DraftableOutput);
        await AddSponsorForReviewAsync(UpdatedAt.AddDays(1));

        await Assert.ThrowsAsync<ReviewConflictException>(() => DecideAsync(1, 0, ReviewDecisionKind.Approve));
        await DecideAsync(2, 0, ReviewDecisionKind.Approve);

        Assert.Equal(2, await ScalarAsync("SELECT revision_number FROM action_review_decisions"));
    }

    [Fact]
    public async Task APersonSaveMayKeepOrClearTheSuggestionButTheStoreRefusesAChangedOne()
    {
        await DraftForReviewAsync(UpdatedAt, DraftableOutput);
        await SaveAsync(1, SuggestionChoice.Keep, "save-2");
        await DraftForReviewAsync(UpdatedAt.AddDays(1), DraftableOutput, title: "A renamed bill");
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
        await DraftForReviewAsync(UpdatedAt, DraftableOutput);
        await SaveAsync(1, SuggestionChoice.Keep, "save-2");
        await DraftForReviewAsync(UpdatedAt.AddDays(1), DraftableOutput, title: "A renamed bill");
        var suggestion = (await ActionReviews().GetRevisionAsync(RecordId, 3, default))!.Suggestion!;

        var used = await SaveAsync(3, SuggestionChoice.Use, "save-4", headline: "Posted headline", summary: "Posted summary.");

        Assert.Equal((suggestion.Headline, suggestion.Summary, ReviewTextOrigin.Person), (used.Headline, used.Summary, used.TextOrigin));
        Assert.Null(used.Suggestion);
    }

    [Fact]
    public async Task UsingASuggestionThatTheRevisionDoesNotHaveIsRefused()
    {
        await DraftForReviewAsync(UpdatedAt, DraftableOutput);

        await Assert.ThrowsAsync<ArgumentException>(() => SaveAsync(1, SuggestionChoice.Use, "save-2"));
    }

    [Fact]
    public async Task TheDatabaseRefusesAnActionStatusThatThePolicyNeverStores()
    {
        await DraftForReviewAsync(UpdatedAt, DraftableOutput);

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

    private async Task<IReadOnlyList<ReviewDecision>> ActionDecisionsAsync()
    {
        var json = (string)(await ScalarAsync("SELECT coalesce(jsonb_agg(decision_json::jsonb ORDER BY review_state_version), '[]')::text FROM action_review_decisions"))!;
        return System.Text.Json.JsonSerializer.Deserialize<ReviewDecision[]>(json, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
    }

    private async Task DraftForReviewAsync(DateTimeOffset updatedAt, string output, string? title = null)
    {
        await DraftAsync(updatedAt, output, title);
        await ReopenAsync();
    }

    private async Task AddSponsorForReviewAsync(DateTimeOffset updatedAt)
    {
        await AddSponsorAsync(updatedAt);
        await ReopenAsync();
    }

    // Puts the record in the state of a worker revision that code did not approve, for tests of the person flow.
    private Task ReopenAsync() => ExecuteAsync(connectionString, $"""
        WITH removed AS (
            DELETE FROM action_review_decisions d USING action_drafts r
             WHERE d.record_id = r.record_id AND d.record_id = '{RecordId}' AND d.review_state_version = r.review_state_version
               AND d.decision_json::jsonb ->> 'actorSubject' = '{ReviewAuthor.CodeSubject}'
         RETURNING 1)
        UPDATE action_drafts SET review_state_version = review_state_version - 1, review_status = 'AwaitingReview'
         WHERE record_id = '{RecordId}' AND EXISTS (SELECT 1 FROM removed)
        """);

    private Task<ReviewDecision> DecideAsync(int revision, int stateVersion, ReviewDecisionKind kind, string? note = null) =>
        new DecideActionReview(ActionReviews()).ExecuteAsync(Reviewer,
            new(RecordId, revision, stateVersion, kind, note, [], Guid.NewGuid().ToString("N")));

    private Task<ActionDraftRevision> SaveAsync(int expected, SuggestionChoice suggestion, string key,
        string headline = "Person headline", string summary = "Person summary.") =>
        new SaveActionDraft(ActionReviews()).ExecuteAsync(Reviewer,
            new(RecordId, expected, headline, summary, suggestion, key));
}
