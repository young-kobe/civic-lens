using CivicLens.Application.Analysis;
using CivicLens.Core.Analysis;
using CivicLens.Application.Actions;
using CivicLens.Application.Review;
using CivicLens.Core.Actions;
using CivicLens.Core.Review;
using CivicLens.Infrastructure.Analysis;
using CivicLens.Infrastructure.Review;
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
    private static readonly ReviewActor Owner = new("auth0|owner", ReviewRole.Owner);

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
        Assert.Equal(ReviewAuthor.CodeSubject, await ScalarAsync("SELECT actor_subject FROM action_review_decisions"));
        Assert.Equal("Approved", await ScalarAsync($"SELECT review_status FROM action_drafts WHERE record_id = '{RecordId}'"));
        Assert.Equal(RecordId, Assert.Single(await ActionReviews().ListUnpublishedApprovedAsync(10, [], default)).Action.RecordId);
    }

    [Fact]
    public async Task ARevisionStoredBeforeTheModelChoiceHasNoCandidateSetAndStillPublishesOpensAndTakesAPersonEdit()
    {
        await DraftAsync(UpdatedAt, DraftableOutput);
        await ExecuteAsync(connectionString, "UPDATE action_revisions SET revision_json = (revision_json::jsonb - 'issueCandidateIds')::text");
        var reviews = ActionReviews();

        var unpublished = Assert.Single(await reviews.ListUnpublishedApprovedAsync(10, [], default));
        var publishable = await reviews.GetForPublicationAsync([RecordId], default);
        var read = await reviews.GetAsync(RecordId, default);
        var edited = await SaveAsync(1, SuggestionChoice.Keep, "save-legacy");

        Assert.True(unpublished.Action.CurrentRevision.IssueCandidateIds.IsDefault);
        Assert.Equal(RecordId, Assert.Single(publishable).Key);
        Assert.Equal(1, read!.Review.CurrentRevision.RevisionNumber);
        Assert.Equal((2, ReviewTextOrigin.Person), (edited.RevisionNumber, edited.TextOrigin));
        Assert.Equal(["rent"], edited.TrackedIssueIds.ToArray());
        Assert.True(edited.IssueCandidateIds.IsDefault);
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
        Assert.Equal("AwaitingReview", await ScalarAsync("SELECT review_status FROM action_drafts"));
        Assert.Equal("An action citation names a missing extraction.", await HeldReasonAsync());
    }

    [Fact]
    public async Task APersonDecisionOrEditRemovesTheHeldReasonOfAnActionBecauseItDescribesTheNewestRevisionOnly()
    {
        var candidate = await CandidateWithoutSummariesAsync();
        await SettleAsync(candidate, Revision(candidate, 1));
        Assert.NotNull(await HeldReasonAsync());

        await DecideAsync(1, 0, ReviewDecisionKind.RequestChanges, "Add the summary.");
        Assert.Null(await HeldReasonAsync());
        await ExecuteAsync(connectionString, "UPDATE action_drafts SET held_reason = 'stale'");
        await SaveAsync(1, SuggestionChoice.Keep, "save-2");

        Assert.Null(await HeldReasonAsync());
    }

    [Fact]
    public async Task ACodeThatLeavesARevisionForAnOpenConcernStoresNoHeldReasonBecauseTheStatusShowsTheConcern()
    {
        var candidate = await CandidateWithoutSummariesAsync();
        await SettleAsync(candidate, Revision(candidate, 1));
        await DecideAsync(1, 0, ReviewDecisionKind.RequestChanges, "Add the summary.");
        await ExecuteAsync(connectionString, $"UPDATE action_analysis SET status = 'Pending' WHERE candidate_id = '{candidate}'");

        await SettleAsync(candidate, Revision(candidate, 2));

        Assert.Equal("AwaitingReviewWithConcerns", await ScalarAsync($"SELECT review_status FROM action_drafts WHERE record_id = '{RecordId}'"));
        Assert.Null(await HeldReasonAsync());
    }

    [Fact]
    public async Task ACodeThatLeavesARevisionForTextAPersonWroteStoresNoHeldReasonBecauseTheStatusShowsTheText()
    {
        var candidate = await CandidateWithoutSummariesAsync();
        await SettleAsync(candidate, Revision(candidate, 1));
        await ExecuteAsync(connectionString, $"UPDATE action_analysis SET status = 'Pending' WHERE candidate_id = '{candidate}'");

        await SettleAsync(candidate, Revision(candidate, 2) with { TextOrigin = ReviewTextOrigin.Person });

        Assert.Equal(2, await ScalarAsync($"SELECT current_revision_number FROM action_drafts WHERE record_id = '{RecordId}'"));
        Assert.Null(await HeldReasonAsync());
    }

    [Fact]
    public async Task ACodeApprovalClearsTheHeldReasonOfAnEarlierRevision()
    {
        await DraftAsync(UpdatedAt, DraftableOutput);
        await ExecuteAsync(connectionString, "UPDATE action_drafts SET held_reason = 'stale'");

        await AddSponsorAsync(UpdatedAt.AddDays(1));

        Assert.Equal("Approved", await ScalarAsync($"SELECT review_status FROM action_drafts WHERE record_id = '{RecordId}'"));
        Assert.Null(await HeldReasonAsync());
    }

    [Fact]
    public async Task APersonEditAfterACodeApprovalWaitsForAPersonAndKeepsTheCodeApprovedRevisionAsTheLastApprovedOne()
    {
        await DraftAsync(UpdatedAt, DraftableOutput);

        await SaveAsync(1, SuggestionChoice.Keep, "save-2");

        Assert.Single(await ActionDecisionsAsync());
        Assert.Equal("AwaitingReview", await ScalarAsync($"SELECT review_status FROM action_drafts WHERE record_id = '{RecordId}'"));
        Assert.Equal(2, await ScalarAsync("SELECT current_revision_number FROM action_drafts"));
        Assert.Equal(1, Assert.Single(await ActionDecisionsAsync()).RevisionNumber);
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
    public async Task ARejectionWithoutAReasonCodeIsRefusedAndWritesNoDecision()
    {
        await DraftForReviewAsync(UpdatedAt, DraftableOutput);

        await Assert.ThrowsAsync<ArgumentException>(() => DecideAsync(1, 0, ReviewDecisionKind.Reject));

        Assert.Empty(await ActionDecisionsAsync());
    }

    [Fact]
    public async Task AnApprovedRevisionCannotBeRejectedBecauseAnApprovalUsesWithdraw()
    {
        await DraftAsync(UpdatedAt, DraftableOutput);

        await Assert.ThrowsAsync<ArgumentException>(() => DecideAsync(1, 1, ReviewDecisionKind.Reject, reason: ReviewReasonCode.Duplicate));

        Assert.Equal("Approved", await ScalarAsync($"SELECT review_status FROM action_drafts WHERE record_id = '{RecordId}'"));
        Assert.Single(await ActionDecisionsAsync());
    }

    [Fact]
    public async Task ARejectedPersonEditStoresItsReasonNeverPublishesAndAPersonCanReverseItWithBothDecisionsKept()
    {
        await DraftAsync(UpdatedAt, DraftableOutput);
        await SaveAsync(1, SuggestionChoice.Keep, "save-2");

        await DecideAsync(2, 1, ReviewDecisionKind.Reject, reason: ReviewReasonCode.InaccurateSummary);

        Assert.Equal("Rejected", await ScalarAsync($"SELECT review_status FROM action_drafts WHERE record_id = '{RecordId}'"));
        Assert.Equal("InaccurateSummary", await ScalarAsync("SELECT reason_code FROM action_review_decisions WHERE kind = 'Reject'"));
        var listed = Assert.Single((await new PostgresReviewQueueStore(AnalysisDbContextFactory.Create(connectionString)).ListRejectedAsync(null, 10, default)).Items);
        Assert.Equal((PublishedRecordKind.Action, RecordId, ReviewReasonCode.InaccurateSummary), (listed.Kind, listed.RecordId, listed.RejectReason));
        Assert.Empty(await ActionReviews().ListUnpublishedApprovedAsync(10, [], default));

        await DecideAsync(2, 2, ReviewDecisionKind.Approve);

        Assert.Equal("Approved", await ScalarAsync($"SELECT review_status FROM action_drafts WHERE record_id = '{RecordId}'"));
        Assert.Equal([ReviewDecisionKind.Approve, ReviewDecisionKind.Reject, ReviewDecisionKind.Approve],
            (await ActionDecisionsAsync()).Select(decision => decision.Kind));
    }

    [Fact]
    public async Task ARejectedRecordStaysRejectedWhenTheWorkerDraftsAgainBecauseTheWorkerCannotReopenAPersonDecision()
    {
        await DraftForReviewAsync(UpdatedAt, DraftableOutput);
        await DecideAsync(1, 0, ReviewDecisionKind.Reject, reason: ReviewReasonCode.Duplicate);

        await AddSponsorAsync(UpdatedAt.AddDays(1));

        Assert.Equal("Rejected", await ScalarAsync($"SELECT review_status FROM action_drafts WHERE record_id = '{RecordId}'"));
        Assert.Equal(1, await ScalarAsync("SELECT current_revision_number FROM action_drafts"));
        Assert.Equal(ReviewDecisionKind.Reject, Assert.Single(await ActionDecisionsAsync()).Kind);
    }

    [Fact]
    public async Task ARejectionThatLandsWhileTheModelRunsDiscardsTheDraftBecauseTheAppendStepRefusesARejectedRecord()
    {
        await DraftForReviewAsync(UpdatedAt, DraftableOutput);
        var newer = await ReadableCandidateAsync("A000001", UpdatedAt.AddDays(1), title: "A renamed bill");
        var model = new FakeHeadlineSummaryModel().Reply(async _ =>
        {
            await ExecuteAsync(connectionString, $"UPDATE action_drafts SET review_status = 'Rejected' WHERE record_id = '{RecordId}'");
            return new("end_turn", DraftableOutput, new(900, 100, 0, 0), "req_fixture");
        });

        Assert.Single((await RunActionWorkerAsync(model)).Requests);

        Assert.Equal("Rejected", await ScalarAsync($"SELECT review_status FROM action_drafts WHERE record_id = '{RecordId}'"));
        Assert.Equal(1, await ScalarAsync("SELECT current_revision_number FROM action_drafts"));
        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM action_analysis_runs WHERE outcome = 'DraftDiscarded'"));
        await AssertBlockAsync(newer);
    }

    private async Task<string> RejectAndRefuseANewerUpdateAsync()
    {
        await DraftForReviewAsync(UpdatedAt, DraftableOutput);
        await DecideAsync(1, 0, ReviewDecisionKind.Reject, reason: ReviewReasonCode.Duplicate);
        var newer = await ReadableCandidateAsync("A000001", UpdatedAt.AddDays(1), title: "A renamed bill");
        Assert.Empty((await RunActionWorkerAsync(new FakeHeadlineSummaryModel())).Requests);
        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM action_analysis_runs WHERE reason = 'recordRejected'"));
        await AssertBlockAsync(newer);
        return newer;
    }

    private async Task AssertBlockAsync(string? candidate) =>
        Assert.Equal(candidate, await ScalarAsync("SELECT refused_candidate_id FROM action_drafts") as string);

    private async Task<AnalysisClaim> ClaimAfterRequeueAsync(string candidate)
    {
        Assert.Equal(1, await new RequeueRefusedActionDrafts(ActionStore()).ExecuteAsync(Owner));
        return (await ActionStore().TryClaimAsync(candidate, Lease, default))!;
    }

    private async Task AssertApprovalStillRefusedAsync(ReviewDecision changes) =>
        await Assert.ThrowsAsync<StaleApprovalException>(() => new DecideActionReview(ActionReviews()).ExecuteAsync(Reviewer,
            new(RecordId, 1, 2, ReviewDecisionKind.Approve, "Looked again.", [changes.DecisionId], Guid.NewGuid().ToString("N"))));

    [Fact]
    public async Task AConflictRetryKeepsTheBlockEvenAfterTheRetryFailsBecauseOnlyANewDraftMayEndIt()
    {
        var newer = await RejectAndRefuseANewerUpdateAsync();
        var changes = await DecideAsync(1, 1, ReviewDecisionKind.RequestChanges, "Look again.");
        var claim = await ClaimAfterRequeueAsync(newer);
        var runId = await ReserveAsync(ActionStore(), claim);
        Assert.True(await ActionStore().CheckpointAsync(ActionDraftCheckpoint.From(claim, Revision(newer, 5)), DraftedRun(runId, newer), default));
        await AssertBlockAsync(newer);
        await AssertApprovalStillRefusedAsync(changes);

        await ExecuteAsync(connectionString, $"UPDATE action_analysis SET status = 'Pending', retry_at = NULL WHERE candidate_id = '{newer}'");
        _ = await RunActionWorkerAsync(new FakeHeadlineSummaryModel().Draft("not json").Draft("not json").Draft("not json"));

        await AssertBlockAsync(newer);
        await AssertApprovalStillRefusedAsync(changes);
    }

    [Fact]
    public async Task ABypassedConflictKeepsTheBlockBecauseABypassedRunIsNotANewDraft()
    {
        var newer = await RejectAndRefuseANewerUpdateAsync();
        await DecideAsync(1, 1, ReviewDecisionKind.RequestChanges, "Look again.");
        var claim = await ClaimAfterRequeueAsync(newer);
        var bypassed = DraftedRun(Guid.NewGuid().ToString("N"), newer);
        bypassed = bypassed with { Run = bypassed.Run with { Outcome = AnalysisRunOutcome.Bypassed, ChargedTokens = 0, Usage = AnalysisTokenUsage.None } };

        Assert.True(await ActionStore().CheckpointAsync(ActionDraftCheckpoint.From(claim, Revision(newer, 5)), bypassed, default));

        await AssertBlockAsync(newer);
    }

    [Theory]
    [InlineData("Pending")]
    [InlineData("Blocked")]
    [InlineData("Failed")]
    public async Task ASupersededDiscardKeepsTheBlockWhateverTheNewestUpdateDoesBecauseNoDraftExistsYet(string newestStatus)
    {
        var newer = await RejectAndRefuseANewerUpdateAsync();
        var newest = await ReadableCandidateAsync("A000001", UpdatedAt.AddDays(2), title: "A third title");
        await ExecuteAsync(connectionString, $"UPDATE action_analysis SET status = '{newestStatus}' WHERE candidate_id = '{newest}'");
        await DecideAsync(1, 1, ReviewDecisionKind.RequestChanges, "Look again.");
        var claim = await ClaimAfterRequeueAsync(newer);

        Assert.True(await ActionStore().CheckpointAsync(AnalysisCheckpoint.From(claim, AnalysisState.Succeeded, ActionDraftErrorCodes.SupersededByNewerCandidate), null, default));

        await AssertBlockAsync(newer);
        Assert.Equal(0, await new RequeueRefusedActionDrafts(ActionStore()).ExecuteAsync(Owner));
    }

    [Fact]
    public async Task TheRefusedRequeueSkipsARecordThatIsStillRejectedSoItsCountReachesZeroUntilRequestChanges()
    {
        await RejectAndRefuseANewerUpdateAsync();

        Assert.Equal(0, await new RequeueRefusedActionDrafts(ActionStore()).ExecuteAsync(Owner));

        await DecideAsync(1, 1, ReviewDecisionKind.RequestChanges, "Look again.");
        Assert.Equal(1, await new RequeueRefusedActionDrafts(ActionStore()).ExecuteAsync(Owner));
    }

    [Fact]
    public async Task ASettlementOfAnUnchangedUpdateOfARejectedRecordWaitsForTheReviewKeyBecauseItMustNotReadNoBlockWhileARefusalCommits()
    {
        await DraftForReviewAsync(UpdatedAt, DraftableOutput);
        await DecideAsync(1, 0, ReviewDecisionKind.Reject, reason: ReviewReasonCode.Duplicate);
        var second = await ReadableCandidateAsync("A000001", UpdatedAt.AddDays(1), title: "A renamed bill");
        var third = await ReadableCandidateAsync("A000001", UpdatedAt.AddDays(2), title: "A third title");
        await using var holder = await HeldAdvisoryLock.AcquireAsync(connectionString, "civic-lens-action-review");

        var settling = SettleBypassedAsync(third, ActionDraftErrorCodes.Unchanged, ActionDraftErrorCodes.Unchanged);
        await HeldAdvisoryLock.WaitForWaiterAsync(connectionString, "civic-lens-action-review");
        await ExecuteAsync(connectionString, $"UPDATE action_drafts SET refused_candidate_id = '{second}'");

        Assert.False(settling.IsCompleted);
        await holder.DisposeAsync();
        await settling;
        await AssertBlockAsync(null);
    }

    [Fact]
    public async Task AnUpdateWithNoTrackedOfficialEndsTheBlockBecauseThenNothingOfTheBillIsTracked()
    {
        await RejectAndRefuseANewerUpdateAsync();
        _ = await ReadableCandidateAsync("Z000009", UpdatedAt.AddDays(2));

        Assert.Empty((await RunActionWorkerAsync(new FakeHeadlineSummaryModel())).Requests);

        await AssertBlockAsync(null);
        await DecideAsync(1, 1, ReviewDecisionKind.Approve);
    }

    private async Task SettleBypassedAsync(string candidate, string code, string reason)
    {
        var claim = (await ActionStore().TryClaimAsync(candidate, Lease, default))!;
        var bypass = DraftedRun(Guid.NewGuid().ToString("N"), candidate);
        bypass = bypass with { Run = bypass.Run with { Outcome = AnalysisRunOutcome.Bypassed, ChargedTokens = 0, Usage = AnalysisTokenUsage.None }, Reason = reason };
        Assert.True(await ActionStore().CheckpointAsync(AnalysisCheckpoint.From(claim, AnalysisState.Succeeded, code), bypass, default));
    }

    [Fact]
    public async Task ARefusalOfTheCandidateOfTheRevisionItselfSetsNoBlockBecauseItIsNoNewerUpdate()
    {
        await DraftForReviewAsync(UpdatedAt, DraftableOutput);
        await DecideAsync(1, 0, ReviewDecisionKind.Reject, reason: ReviewReasonCode.Duplicate);
        var own = (string)(await ScalarAsync("SELECT candidate_id FROM action_drafts"))!;
        await ExecuteAsync(connectionString, $"UPDATE action_analysis SET status = 'Pending' WHERE candidate_id = '{own}'");

        await SettleBypassedAsync(own, ActionDraftErrorCodes.Unchanged, ActionDraftErrorCodes.RecordRejected);

        await AssertBlockAsync(null);
    }

    [Fact]
    public async Task ARefusalThatMeetsANewerUpdateAtTheSettlementIsSupersededWithNoRunAndNoBlockBecauseTheNewerUpdateDecides()
    {
        await DraftForReviewAsync(UpdatedAt, DraftableOutput);
        await DecideAsync(1, 0, ReviewDecisionKind.Reject, reason: ReviewReasonCode.Duplicate);
        var second = await ReadableCandidateAsync("A000001", UpdatedAt.AddDays(1), title: "A renamed bill");
        var claim = (await ActionStore().TryClaimAsync(second, Lease, default))!;
        _ = await ReadableCandidateAsync("A000001", UpdatedAt.AddDays(2), title: "A third title");
        var bypass = DraftedRun(Guid.NewGuid().ToString("N"), second);
        bypass = bypass with { Run = bypass.Run with { Outcome = AnalysisRunOutcome.Bypassed, ChargedTokens = 0, Usage = AnalysisTokenUsage.None }, Reason = ActionDraftErrorCodes.RecordRejected };

        Assert.True(await ActionStore().CheckpointAsync(AnalysisCheckpoint.From(claim, AnalysisState.Succeeded, ActionDraftErrorCodes.Unchanged), bypass, default));

        await AssertBlockAsync(null);
        Assert.Equal(0L, await ScalarAsync($"SELECT count(*) FROM action_analysis_runs WHERE candidate_id = '{second}'"));
        Assert.Equal(ActionDraftErrorCodes.SupersededByNewerCandidate, await ScalarAsync($"SELECT error_code FROM action_analysis WHERE candidate_id = '{second}'"));
    }

    [Fact]
    public async Task ALateRefusalOfAnOlderUpdateLeavesTheBlockOfTheNewerOneBecauseTheNewestRefusedUpdateMustStayNamed()
    {
        await DraftForReviewAsync(UpdatedAt, DraftableOutput);
        await DecideAsync(1, 0, ReviewDecisionKind.Reject, reason: ReviewReasonCode.Duplicate);
        var second = await ReadableCandidateAsync("A000001", UpdatedAt.AddDays(1), title: "A renamed bill");
        var third = await ReadableCandidateAsync("A000001", UpdatedAt.AddDays(2), title: "A third title");
        await SettleBypassedAsync(third, ActionDraftErrorCodes.Unchanged, ActionDraftErrorCodes.RecordRejected);
        await AssertBlockAsync(third);

        await SettleBypassedAsync(second, ActionDraftErrorCodes.Unchanged, ActionDraftErrorCodes.RecordRejected);

        await AssertBlockAsync(third);
    }

    [Fact]
    public async Task ALateUnchangedSettlementOfAnOlderUpdateKeepsTheBlockBecauseOnlyAnUpdateNotOlderThanTheRefusedOneMayEndIt()
    {
        await DraftForReviewAsync(UpdatedAt, DraftableOutput);
        await DecideAsync(1, 0, ReviewDecisionKind.Reject, reason: ReviewReasonCode.Duplicate);
        var second = await ReadableCandidateAsync("A000001", UpdatedAt.AddDays(1), title: "A renamed bill");
        var third = await ReadableCandidateAsync("A000001", UpdatedAt.AddDays(2), title: "A third title");
        await SettleBypassedAsync(third, ActionDraftErrorCodes.Unchanged, ActionDraftErrorCodes.RecordRejected);

        await SettleBypassedAsync(second, ActionDraftErrorCodes.Unchanged, ActionDraftErrorCodes.Unchanged);

        await AssertBlockAsync(third);
    }

    [Fact]
    public async Task AnOlderLateUpdateNeverSetsTheBlockBecauseTheRevisionAlreadyHoldsNewerFacts()
    {
        await DraftForReviewAsync(UpdatedAt, DraftableOutput);
        await DecideAsync(1, 0, ReviewDecisionKind.Reject, reason: ReviewReasonCode.Duplicate);
        var older = await ReadableCandidateAsync("A000001", UpdatedAt.AddDays(-1), title: "An older title");
        var claim = (await ActionStore().TryClaimAsync(older, Lease, default))!;
        var bypass = DraftedRun(Guid.NewGuid().ToString("N"), older);
        bypass = bypass with { Run = bypass.Run with { Outcome = AnalysisRunOutcome.Bypassed, ChargedTokens = 0, Usage = AnalysisTokenUsage.None }, Reason = ActionDraftErrorCodes.RecordRejected };

        Assert.True(await ActionStore().CheckpointAsync(AnalysisCheckpoint.From(claim, AnalysisState.Succeeded, ActionDraftErrorCodes.Unchanged), bypass, default));

        await AssertBlockAsync(null);
    }

    [Fact]
    public async Task ApprovingIsRefusedWhileTheWorkerRefusedANewerCandidateAndTheMessageNamesTheCommandBecauseTheOldRevisionHoldsOldFacts()
    {
        await RejectAndRefuseANewerUpdateAsync();

        var refused = await Assert.ThrowsAsync<StaleApprovalException>(() => DecideAsync(1, 1, ReviewDecisionKind.Approve));

        Assert.Contains("actions draft requeue --refused", refused.Message, StringComparison.Ordinal);
        Assert.Equal("Rejected", await ScalarAsync($"SELECT review_status FROM action_drafts WHERE record_id = '{RecordId}'"));
        Assert.Equal(ReviewDecisionKind.Reject, Assert.Single(await ActionDecisionsAsync()).Kind);
    }

    [Fact]
    public async Task RequestChangesThenApproveIsRefusedToo()
    {
        await RejectAndRefuseANewerUpdateAsync();
        var changes = await DecideAsync(1, 1, ReviewDecisionKind.RequestChanges, "Look again.");

        await Assert.ThrowsAsync<StaleApprovalException>(() => new DecideActionReview(ActionReviews()).ExecuteAsync(Reviewer,
            new(RecordId, 1, 2, ReviewDecisionKind.Approve, "Looked again.", [changes.DecisionId], Guid.NewGuid().ToString("N"))));

        Assert.Equal(2, (await ActionDecisionsAsync()).Count);
    }

    [Fact]
    public async Task APersonEditDoesNotOverrideTheRefusalForPlainApproveAndForSaveAndApprove()
    {
        await RejectAndRefuseANewerUpdateAsync();
        var edited = await SaveAsync(1, SuggestionChoice.Keep, "save-2");

        await Assert.ThrowsAsync<StaleApprovalException>(() => DecideAsync(edited.RevisionNumber, 1, ReviewDecisionKind.Approve));
        await Assert.ThrowsAsync<StaleApprovalException>(() => new SaveActionDraft(ActionReviews()).ExecuteAsync(Reviewer,
            new(RecordId, edited.RevisionNumber, edited.Headline + " again", edited.Summary, SuggestionChoice.Keep, "edit-again",
                new ApprovalWithSave(1, null, [], null))));

        Assert.Equal(2, await ScalarAsync("SELECT current_revision_number FROM action_drafts"));
    }

    [Fact]
    public async Task AnUpdateWithTheSameFactsLeavesNoBlockSoReversingStillWorks()
    {
        await DraftForReviewAsync(UpdatedAt, DraftableOutput);
        await DecideAsync(1, 0, ReviewDecisionKind.Reject, reason: ReviewReasonCode.Duplicate);
        _ = await ReadableCandidateAsync("A000001", UpdatedAt.AddDays(1));
        Assert.Empty((await RunActionWorkerAsync(new FakeHeadlineSummaryModel())).Requests);
        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM action_analysis_runs WHERE reason = 'recordRejected'"));

        await DecideAsync(1, 1, ReviewDecisionKind.Approve);

        Assert.Equal("Approved", await ScalarAsync($"SELECT review_status FROM action_drafts WHERE record_id = '{RecordId}'"));
    }

    [Fact]
    public async Task ReversingStillWorksWithARealOlderCandidateAndANewerOneThatIsStillPending()
    {
        await DraftForReviewAsync(UpdatedAt, DraftableOutput);
        await DecideAsync(1, 0, ReviewDecisionKind.Reject, reason: ReviewReasonCode.Duplicate);
        _ = await ReadableCandidateAsync("A000001", UpdatedAt.AddDays(1), title: "A renamed bill");

        await DecideAsync(1, 1, ReviewDecisionKind.Approve);

        Assert.Equal("Approved", await ScalarAsync($"SELECT review_status FROM action_drafts WHERE record_id = '{RecordId}'"));
    }

    [Fact]
    public async Task TheRequeueCommandSendsOnlyRefusedEntriesBackAndANewDraftClearsTheBlockSoApprovingWorksAgain()
    {
        await RejectAndRefuseANewerUpdateAsync();
        var edited = await SaveAsync(1, SuggestionChoice.Keep, "save-2");
        await Assert.ThrowsAsync<StaleApprovalException>(() => DecideAsync(edited.RevisionNumber, 1, ReviewDecisionKind.Approve));

        Assert.Equal(1, await new RequeueRefusedActionDrafts(ActionStore()).ExecuteAsync(Owner));
        _ = await RunActionWorkerAsync(new FakeHeadlineSummaryModel().Draft(DraftableOutput));

        Assert.Equal(0, await new RequeueRefusedActionDrafts(ActionStore()).ExecuteAsync(Owner));
        await AssertBlockAsync(null);
        var latest = await ScalarAsync("SELECT max(current_revision_number) FROM action_drafts");
        await DecideAsync((int)latest!, 1, ReviewDecisionKind.Approve);
        Assert.Equal("Approved", await ScalarAsync($"SELECT review_status FROM action_drafts WHERE record_id = '{RecordId}'"));
    }

    [Fact]
    public async Task AnUpdateWithTheSameFactsEndsTheBlockBecauseNoFactWasRefused()
    {
        await RejectAndRefuseANewerUpdateAsync();
        _ = await ReadableCandidateAsync("A000001", UpdatedAt.AddDays(2));
        Assert.Empty((await RunActionWorkerAsync(new FakeHeadlineSummaryModel())).Requests);

        await AssertBlockAsync(null);
        await DecideAsync(1, 1, ReviewDecisionKind.Approve);

        Assert.Equal("Approved", await ScalarAsync($"SELECT review_status FROM action_drafts WHERE record_id = '{RecordId}'"));
    }

    [Fact]
    public async Task OnlyTheOwnerRunsTheRequeueCommand()
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new RequeueRefusedActionDrafts(ActionStore()).ExecuteAsync(Reviewer));
    }

    [Fact]
    public async Task ABlockIsNotWrittenWhenThePersonChangedTheRecordBeforeTheSettlementBecauseTheWorkerMayDraftIt()
    {
        await DraftForReviewAsync(UpdatedAt, DraftableOutput);
        await DecideAsync(1, 0, ReviewDecisionKind.Reject, reason: ReviewReasonCode.Duplicate);
        var newer = await ReadableCandidateAsync("A000001", UpdatedAt.AddDays(1), title: "A renamed bill");
        var claim = (await ActionStore().TryClaimAsync(newer, Lease, default))!;
        await DecideAsync(1, 1, ReviewDecisionKind.RequestChanges, "Look again.");
        var bypass = new ActionDraftRun(new AnalysisRun(Guid.NewGuid().ToString("N"), newer, ActionDraftTask.Task, ActionDraftTask.TaskVersion,
            ActionDraftTask.Model, ActionDraftTask.PromptVersion, ActionDraftTask.SchemaVersion, new string('1', 64), null, AnalysisRunOutcome.Bypassed,
            new(0, 0, 0, 0), 0, null, null, null, [], "{}", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch), ActionDraftErrorCodes.RecordRejected);

        Assert.True(await ActionStore().CheckpointAsync(AnalysisCheckpoint.From(claim, AnalysisState.Succeeded, ActionDraftErrorCodes.Unchanged), bypass, default));

        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM action_analysis_runs WHERE reason = 'recordRejected'"));
        Assert.Equal(0L, await ScalarAsync($"SELECT count(*) FROM action_analysis_runs WHERE candidate_id = '{newer}'"));
        await AssertBlockAsync(null);
        Assert.Equal("RetryWaiting", await ScalarAsync($"SELECT status FROM action_analysis WHERE candidate_id = '{newer}'"));
    }

    [Fact]
    public async Task ARejectedRecordGetsNoModelCallForANewCandidateBecauseTheTokensWouldBeWasted()
    {
        await DraftForReviewAsync(UpdatedAt, DraftableOutput);
        await DecideAsync(1, 0, ReviewDecisionKind.Reject, reason: ReviewReasonCode.Duplicate);
        _ = await ReadableCandidateAsync("A000001", UpdatedAt.AddDays(1), title: "A renamed bill");

        Assert.Empty((await RunActionWorkerAsync(new FakeHeadlineSummaryModel())).Requests);

        Assert.Equal(1, await ScalarAsync("SELECT current_revision_number FROM action_drafts"));
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
    public async Task ASaveWhoseApprovalFailsBecauseAConcernIsOpenWritesNoRevisionAndNoApproval()
    {
        await DraftForReviewAsync(UpdatedAt, DraftableOutput);
        var concern = await DecideAsync(1, 0, ReviewDecisionKind.RequestChanges, "Fix the summary.");
        var request = new SaveActionDraftRequest(RecordId, 1, "Typed headline", "Typed summary.", SuggestionChoice.Keep, "blocked",
            new ApprovalWithSave(1, "Fixed.", [], null));

        await Assert.ThrowsAsync<ArgumentException>(() => new SaveActionDraft(ActionReviews()).ExecuteAsync(Reviewer, request));

        Assert.Equal(1, await ScalarAsync($"SELECT current_revision_number FROM action_drafts WHERE record_id = '{RecordId}'"));
        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM action_review_decisions"));
        var saved = await new SaveActionDraft(ActionReviews()).ExecuteAsync(Reviewer,
            request with { IdempotencyKey = "resolved", Approval = new(1, "Fixed.", [concern.DecisionId], null) });
        Assert.Equal(2, saved.RevisionNumber);
        Assert.Equal(2, await ScalarAsync("SELECT revision_number FROM action_review_decisions WHERE kind = 'Approve'"));
    }

    [Fact]
    public async Task SaveAndApproveWithNoEditApprovesTheCurrentRevisionAndKeepsItsNumberOriginAndAuthor()
    {
        await DraftForReviewAsync(UpdatedAt, DraftableOutput);
        var before = (await ActionReviews().GetRevisionAsync(RecordId, 1, default))!;

        var result = await SaveUnchangedAndApproveAsync(before, "no-edit");

        var after = (await ActionReviews().GetRevisionAsync(RecordId, 1, default))!;
        Assert.Equal((1, before.TextOrigin, before.AuthorSubject), (result.RevisionNumber, after.TextOrigin, after.AuthorSubject));
        Assert.NotEqual(ReviewTextOrigin.Person, after.TextOrigin);
        Assert.Equal(1, await ScalarAsync($"SELECT current_revision_number FROM action_drafts WHERE record_id = '{RecordId}'"));
        Assert.Equal(1L, await ScalarAsync($"SELECT count(*) FROM action_revisions WHERE record_id = '{RecordId}'"));
        Assert.Equal((1, Reviewer.Subject), ((int)(await ScalarAsync("SELECT revision_number FROM action_review_decisions WHERE kind = 'Approve'"))!,
            (string)(await ScalarAsync("SELECT actor_subject FROM action_review_decisions WHERE kind = 'Approve'"))!));
    }

    [Fact]
    public async Task SaveAndApproveWithNoEditRefusesWhenTheRevisionIsAlreadyApprovedBecauseThereIsNothingToSave()
    {
        await DraftForReviewAsync(UpdatedAt, DraftableOutput);
        var before = (await ActionReviews().GetRevisionAsync(RecordId, 1, default))!;
        await SaveUnchangedAndApproveAsync(before, "first");

        await Assert.ThrowsAsync<NothingToSaveException>(() => SaveUnchangedAndApproveAsync(before, "again", stateVersion: 1));

        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM action_review_decisions"));
        Assert.Equal(1, await ScalarAsync($"SELECT current_revision_number FROM action_drafts WHERE record_id = '{RecordId}'"));
    }

    [Fact]
    public async Task SaveAndApproveWithAnEditWritesAPersonRevisionAndApprovesIt()
    {
        await DraftForReviewAsync(UpdatedAt, DraftableOutput);
        var before = (await ActionReviews().GetRevisionAsync(RecordId, 1, default))!;

        var result = await new SaveActionDraft(ActionReviews()).ExecuteAsync(Reviewer, new(RecordId, 1, before.Headline + " edited",
            before.Summary, SuggestionChoice.Keep, "edit", new ApprovalWithSave(0, null, [], null)));

        Assert.Equal((2, ReviewTextOrigin.Person, Reviewer.Subject), (result.RevisionNumber, result.TextOrigin, result.AuthorSubject));
        Assert.Equal(2, await ScalarAsync("SELECT revision_number FROM action_review_decisions WHERE kind = 'Approve'"));
    }

    private Task<ActionDraftRevision> SaveUnchangedAndApproveAsync(ActionDraftRevision current, string key, int stateVersion = 0) =>
        new SaveActionDraft(ActionReviews()).ExecuteAsync(Reviewer, new(RecordId, current.RevisionNumber, current.Headline, current.Summary,
            SuggestionChoice.Keep, key, new ApprovalWithSave(stateVersion, null, [], null)));

    [Fact]
    public async Task AfterAWithdrawalANoEditPressNeedsTheNoteAndTheResolvedConcernAndStoresTheCorrectionNote()
    {
        await DraftForReviewAsync(UpdatedAt, DraftableOutput);
        var before = (await ActionReviews().GetRevisionAsync(RecordId, 1, default))!;
        await SaveUnchangedAndApproveAsync(before, "first");
        var withdrawal = await DecideAsync(1, 1, ReviewDecisionKind.WithdrawApproval, "Wrong date.");
        var save = new SaveActionDraft(ActionReviews());
        SaveActionDraftRequest Press(string key, ApprovalWithSave approval) =>
            new(RecordId, 1, before.Headline, before.Summary, SuggestionChoice.Keep, key, approval);

        await Assert.ThrowsAsync<ArgumentException>(() => save.ExecuteAsync(Reviewer, Press("no-note", new(2, null, [], null))));

        Assert.Equal(2L, await ScalarAsync("SELECT count(*) FROM action_review_decisions"));
        var result = await save.ExecuteAsync(Reviewer, Press("with-note", new(2, "Date fixed.", [withdrawal.DecisionId], "Clarified the date.")));
        Assert.Equal(1, result.RevisionNumber);
        Assert.Equal(1L, await ScalarAsync($"SELECT count(*) FROM action_revisions WHERE record_id = '{RecordId}'"));
        Assert.Equal("Clarified the date.", await ScalarAsync("SELECT correction_note FROM action_review_decisions WHERE review_state_version = 3"));
    }

    [Fact]
    public async Task RetryingANoEditPressWithTheSameKeyReturnsTheSameRevisionAndWritesOneDecision()
    {
        await DraftForReviewAsync(UpdatedAt, DraftableOutput);
        var before = (await ActionReviews().GetRevisionAsync(RecordId, 1, default))!;

        var first = await SaveUnchangedAndApproveAsync(before, "same-key");
        var second = await SaveUnchangedAndApproveAsync(before, "same-key");

        Assert.Equal((first.RevisionNumber, first.CreatedAtUtc), (second.RevisionNumber, second.CreatedAtUtc));
        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM action_review_decisions"));
    }

    [Fact]
    public async Task ATrailingSpaceIsARealEditSoTheLineBreakRuleCannotHideWordingChanges()
    {
        await DraftForReviewAsync(UpdatedAt, DraftableOutput);
        var before = (await ActionReviews().GetRevisionAsync(RecordId, 1, default))!;

        var result = await new SaveActionDraft(ActionReviews()).ExecuteAsync(Reviewer, new(RecordId, 1, before.Headline,
            before.Summary + " ", SuggestionChoice.Keep, "space", new ApprovalWithSave(0, null, [], null)));

        Assert.Equal((2, ReviewTextOrigin.Person), (result.RevisionNumber, result.TextOrigin));
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
            ActionReviews().SaveRevisionAsync(Reviewer.Subject, forged, 5, null, "save-6", new string('6', 64), default));
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

    private Task<IReadOnlyList<StoredDecision>> ActionDecisionsAsync() => ReviewDecisionRows.ReadAsync(connectionString, "action_review_decisions");

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
               AND d.actor_subject = '{ReviewAuthor.CodeSubject}'
         RETURNING 1)
        UPDATE action_drafts SET review_state_version = review_state_version - 1, review_status = 'AwaitingReview'
         WHERE record_id = '{RecordId}' AND EXISTS (SELECT 1 FROM removed)
        """);

    private async Task<string?> HeldReasonAsync() =>
        await ScalarAsync("SELECT held_reason FROM action_drafts") is string reason ? reason : null;

    private Task<ReviewDecision> DecideAsync(int revision, int stateVersion, ReviewDecisionKind kind, string? note = null,
        ReviewReasonCode? reason = null) =>
        new DecideActionReview(ActionReviews()).ExecuteAsync(Reviewer,
            new(RecordId, revision, stateVersion, kind, note, [], Guid.NewGuid().ToString("N"), ReasonCode: reason));

    private Task<ActionDraftRevision> SaveAsync(int expected, SuggestionChoice suggestion, string key,
        string headline = "Person headline", string summary = "Person summary.") =>
        new SaveActionDraft(ActionReviews()).ExecuteAsync(Reviewer,
            new(RecordId, expected, headline, summary, suggestion, key));
}
