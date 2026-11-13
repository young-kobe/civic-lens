using CivicLens.Core.Analysis;
using CivicLens.Application.Analysis;
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
        var vote = await RevisionAsync(candidate, 1);
        await SettleForReviewAsync(candidate, vote);
        var recordId = await RecordIdAsync(candidate);
        var member = vote.Members[0].PersonId;
        await new RelationshipWorldSeeder(connectionString).StatementAsync(RelationshipFixtures.StatementAboutBill(member,
            vote.Congress, vote.BillNumber!.Value, "H.Con.Res."));
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
        var vote = await RevisionAsync(candidate, 1);
        await SettleForReviewAsync(candidate, vote);
        var recordId = await RecordIdAsync(candidate);
        var (member, congress, number) = (vote.Members[0].PersonId,
            vote.Congress, vote.BillNumber!.Value);
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
        var vote = await RevisionAsync(first, 1);
        await SettleAsync(first, vote);
        var recordId = await RecordIdAsync(first);
        var member = vote.Members[0].PersonId;
        await new RelationshipWorldSeeder(connectionString).StatementAsync(RelationshipFixtures.StatementAboutBill(member,
            vote.Congress, vote.BillNumber!.Value, "H.Con.Res."));
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

        await SettleAsync(candidate, revision with { Facts = [new("Result: Passed", revision.Facts[0].Citation with { Length = 1_000_000 })] });

        Assert.Empty(await VoteDecisionsAsync());
        Assert.Equal("AwaitingReview", await ScalarAsync("SELECT review_status FROM vote_drafts"));
        Assert.Equal("Span must fit within the extracted text.", await ScalarAsync("SELECT held_reason FROM vote_drafts"));
    }

    [Theory]
    [InlineData(GradePassages.PassingHeadline, "Approved", null)]
    [InlineData(GradePassages.FailingHeadline, "AwaitingReview", GradePassages.ApprovalMessage)]
    public async Task CodeApprovesVoteTextAtThirteenPointFiveAndHoldsTextAtFourteenPointOneForAPersonWithTheGrade(string headline, string status,
        string? heldReason)
    {
        var candidate = await PendingCandidateAsync();

        await SettleAsync(candidate, await RevisionAsync(candidate, 1) with { Headline = headline, Summary = GradePassages.Summary });

        Assert.Equal((status, heldReason), ((string)(await ScalarAsync("SELECT review_status FROM vote_drafts"))!,
            await ScalarAsync("SELECT held_reason FROM vote_drafts") as string));
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
    public async Task ARejectedVoteStaysRejectedWhenTheWorkerRevisesItBecauseTheWorkerCannotReopenAPersonDecision()
    {
        var first = await PendingCandidateAsync();
        await SettleForReviewAsync(first, await RevisionAsync(first, 1));
        var recordId = await RecordIdAsync(first);
        await DecideAsync(recordId, 1, 0, ReviewDecisionKind.Reject, reason: ReviewReasonCode.Duplicate);

        var second = await PendingCandidateAsync(Updated.AddDays(1));
        await SettleAsync(second, await RevisionAsync(second, 2));

        Assert.Equal("Rejected", await ScalarAsync($"SELECT review_status FROM vote_drafts WHERE record_id = '{recordId}'"));
        Assert.Equal(1, await ScalarAsync($"SELECT current_revision_number FROM vote_drafts WHERE record_id = '{recordId}'"));
        Assert.Equal(ReviewDecisionKind.Reject, Assert.Single(await VoteDecisionsAsync()).Kind);
        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM vote_analysis_runs WHERE outcome = 'DraftDiscarded'"));
    }

    [Fact]
    public async Task ReversingARejectedVoteIsRefusedWhenTheWorkerRefusedANewerCandidateBecauseTheOldRevisionHoldsOldFacts()
    {
        var first = await PendingCandidateAsync();
        await SettleForReviewAsync(first, await RevisionAsync(first, 1));
        var recordId = await RecordIdAsync(first);
        await DecideAsync(recordId, 1, 0, ReviewDecisionKind.Reject, reason: ReviewReasonCode.Duplicate);
        var second = await PendingCandidateAsync(Updated.AddDays(1));
        await SettleAsync(second, await RevisionAsync(second, 2));
        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM vote_analysis_runs WHERE reason = 'recordRejected'"));

        var refused = await Assert.ThrowsAsync<StaleApprovalException>(() => DecideAsync(recordId, 1, 1, ReviewDecisionKind.Approve));

        Assert.Contains("votes draft requeue --refused", refused.Message, StringComparison.Ordinal);
        Assert.Equal("Rejected", await ScalarAsync($"SELECT review_status FROM vote_drafts WHERE record_id = '{recordId}'"));
    }

    [Fact]
    public async Task RequestChangesThenApproveAndAPersonEditAreRefusedToo()
    {
        var first = await PendingCandidateAsync();
        await SettleForReviewAsync(first, await RevisionAsync(first, 1));
        var recordId = await RecordIdAsync(first);
        await DecideAsync(recordId, 1, 0, ReviewDecisionKind.Reject, reason: ReviewReasonCode.Duplicate);
        var second = await PendingCandidateAsync(Updated.AddDays(1));
        await SettleAsync(second, await RevisionAsync(second, 2));
        var changes = await DecideAsync(recordId, 1, 1, ReviewDecisionKind.RequestChanges, "Look again.");

        await Assert.ThrowsAsync<StaleApprovalException>(() => new DecideVoteReview(VoteReviews()).ExecuteAsync(Reviewer,
            new(recordId, 1, 2, ReviewDecisionKind.Approve, "Looked again.", [changes.DecisionId], Guid.NewGuid().ToString("N"))));
        var edited = await SaveAsync(recordId, 1, SuggestionChoice.Keep, "save-2");
        await Assert.ThrowsAsync<StaleApprovalException>(() => new DecideVoteReview(VoteReviews()).ExecuteAsync(Reviewer,
            new(recordId, edited.RevisionNumber, 2, ReviewDecisionKind.Approve, "Looked again.", [changes.DecisionId], Guid.NewGuid().ToString("N"))));
    }

    private async Task<(string RecordId, string Refused)> RejectAndRefuseANewerUpdateAsync()
    {
        var first = await PendingCandidateAsync();
        await SettleForReviewAsync(first, await RevisionAsync(first, 1));
        var recordId = await RecordIdAsync(first);
        await DecideAsync(recordId, 1, 0, ReviewDecisionKind.Reject, reason: ReviewReasonCode.Duplicate);
        var second = await PendingCandidateAsync(Updated.AddDays(1));
        await SettleAsync(second, await RevisionAsync(second, 2));
        return (recordId, second);
    }

    private Task BypassAsync(string candidate, string code) => SettleBypassedAsync(candidate, code, code);

    private async Task AssertBlockAsync(string? candidate) =>
        Assert.Equal(candidate, await ScalarAsync("SELECT refused_candidate_id FROM vote_drafts") as string);

    private async Task<AnalysisClaim> ClaimAfterRequeueAsync(string candidate)
    {
        Assert.Equal(1, await new RequeueRefusedVoteDrafts(VoteStore()).ExecuteAsync(new ReviewActor("auth0|owner", ReviewRole.Owner)));
        return (await VoteStore().TryClaimAsync(candidate, Lease, default))!;
    }

    private Task AssertApprovalStillRefusedAsync(string recordId, ReviewDecision changes) =>
        Assert.ThrowsAsync<StaleApprovalException>(() => new DecideVoteReview(VoteReviews()).ExecuteAsync(Reviewer,
            new(recordId, 1, 2, ReviewDecisionKind.Approve, "Looked again.", [changes.DecisionId], Guid.NewGuid().ToString("N"))));

    private static VoteDraftRun Bypassed(string candidate, string reason) => new(BypassedRun(Guid.NewGuid().ToString("N"), candidate), reason);

    [Fact]
    public async Task AConflictRetryKeepsTheBlockEvenAfterTheRetryFailsBecauseOnlyANewDraftMayEndIt()
    {
        var (recordId, second) = await RejectAndRefuseANewerUpdateAsync();
        await AssertBlockAsync(second);
        var changes = await DecideAsync(recordId, 1, 1, ReviewDecisionKind.RequestChanges, "Look again.");
        var claim = await ClaimAfterRequeueAsync(second);
        var store = VoteStore();
        var runId = await ReserveAsync(store, claim);
        Assert.True(await store.CheckpointAsync(VoteDraftCheckpoint.From(claim, await RevisionAsync(second, 5)), DraftedRun(runId, second), default));
        await AssertBlockAsync(second);
        await AssertApprovalStillRefusedAsync(recordId, changes);

        await ExecuteAsync($"UPDATE vote_analysis SET status = 'Pending', retry_at = NULL WHERE candidate_id = '{second}'");
        var retryClaim = (await store.TryClaimAsync(second, Lease, default))!;
        var failedRunId = await ReserveAsync(store, retryClaim);
        var failed = DraftedRun(failedRunId, second);
        Assert.True(await store.CheckpointAsync(AnalysisCheckpoint.From(retryClaim, AnalysisState.Failed, "outputRejected"),
            failed with { Run = failed.Run with { Outcome = AnalysisRunOutcome.OutputRejected, ValidationErrors = ["The answer was not valid."] } }, default));

        await AssertBlockAsync(second);
        await AssertApprovalStillRefusedAsync(recordId, changes);
    }

    [Fact]
    public async Task ABypassedConflictKeepsTheBlockBecauseABypassedRunIsNotANewDraft()
    {
        var (recordId, second) = await RejectAndRefuseANewerUpdateAsync();
        await DecideAsync(recordId, 1, 1, ReviewDecisionKind.RequestChanges, "Look again.");
        var claim = await ClaimAfterRequeueAsync(second);

        Assert.True(await VoteStore().CheckpointAsync(VoteDraftCheckpoint.From(claim, await RevisionAsync(second, 5)),
            Bypassed(second, VoteDraftErrorCodes.MemberVotesChanged), default));

        await AssertBlockAsync(second);
    }

    [Theory]
    [InlineData("Pending")]
    [InlineData("Blocked")]
    [InlineData("Failed")]
    public async Task ASupersededDiscardKeepsTheBlockWhateverTheNewestUpdateDoesBecauseNoDraftExistsYet(string newestStatus)
    {
        var (recordId, second) = await RejectAndRefuseANewerUpdateAsync();
        var newest = await PendingCandidateAsync(Updated.AddDays(2));
        await ExecuteAsync($"UPDATE vote_analysis SET status = '{newestStatus}' WHERE candidate_id = '{newest}'");
        await DecideAsync(recordId, 1, 1, ReviewDecisionKind.RequestChanges, "Look again.");
        var claim = await ClaimAfterRequeueAsync(second);

        Assert.True(await VoteStore().CheckpointAsync(AnalysisCheckpoint.From(claim, AnalysisState.Succeeded, VoteDraftErrorCodes.SupersededByNewerCandidate), null, default));

        await AssertBlockAsync(second);
        Assert.Equal(0, await new RequeueRefusedVoteDrafts(VoteStore()).ExecuteAsync(new ReviewActor("auth0|owner", ReviewRole.Owner)));
    }

    [Fact]
    public async Task TheRefusedRequeueSkipsARecordThatIsStillRejectedSoItsCountReachesZeroUntilRequestChanges()
    {
        var (recordId, _) = await RejectAndRefuseANewerUpdateAsync();
        var owner = new ReviewActor("auth0|owner", ReviewRole.Owner);

        Assert.Equal(0, await new RequeueRefusedVoteDrafts(VoteStore()).ExecuteAsync(owner));

        await DecideAsync(recordId, 1, 1, ReviewDecisionKind.RequestChanges, "Look again.");
        Assert.Equal(1, await new RequeueRefusedVoteDrafts(VoteStore()).ExecuteAsync(owner));
    }

    [Fact]
    public async Task ASettlementOfAnUnchangedUpdateOfARejectedRecordWaitsForTheReviewKeyBecauseItMustNotReadNoBlockWhileARefusalCommits()
    {
        var (_, second, third) = await RejectWithTwoNewerUpdatesAsync();
        await using var holder = await HeldAdvisoryLock.AcquireAsync(connectionString, "civic-lens-vote-review");

        var settling = SettleBypassedAsync(third, VoteDraftErrorCodes.Unchanged, VoteDraftErrorCodes.Unchanged);
        await HeldAdvisoryLock.WaitForWaiterAsync(connectionString, "civic-lens-vote-review");
        await ExecuteAsync($"UPDATE vote_drafts SET refused_candidate_id = '{second}'");

        Assert.False(settling.IsCompleted);
        await holder.DisposeAsync();
        await settling;
        await AssertBlockAsync(null);
    }

    [Fact]
    public async Task AnUpdateWithNoTrackedMemberEndsTheBlockBecauseThenNothingOfTheVoteIsTracked()
    {
        var (recordId, _) = await RejectAndRefuseANewerUpdateAsync();
        var third = await PendingCandidateAsync(Updated.AddDays(2));
        await BypassAsync(third, VoteDraftErrorCodes.NoTrackedMember);

        await AssertBlockAsync(null);
        await DecideAsync(recordId, 1, 1, ReviewDecisionKind.Approve);
    }

    private async Task<(string RecordId, string Second, string Third)> RejectWithTwoNewerUpdatesAsync()
    {
        var first = await PendingCandidateAsync();
        await SettleForReviewAsync(first, await RevisionAsync(first, 1));
        var recordId = await RecordIdAsync(first);
        await DecideAsync(recordId, 1, 0, ReviewDecisionKind.Reject, reason: ReviewReasonCode.Duplicate);
        var second = await PendingCandidateAsync(Updated.AddDays(1));
        var third = await PendingCandidateAsync(Updated.AddDays(2));
        return (recordId, second, third);
    }

    private async Task SettleBypassedAsync(string candidate, string code, string reason)
    {
        var store = VoteStore();
        var claim = (await store.TryClaimAsync(candidate, Lease, default))!;
        Assert.True(await store.CheckpointAsync(AnalysisCheckpoint.From(claim, AnalysisState.Succeeded, code), Bypassed(candidate, reason), default));
    }

    [Fact]
    public async Task ANoCallRefusalOfARejectedRecordNamesTheNewerUpdateBecauseTheApprovalCheckReadsOnlyTheDraft()
    {
        var first = await PendingCandidateAsync();
        await SettleForReviewAsync(first, await RevisionAsync(first, 1));
        var recordId = await RecordIdAsync(first);
        await DecideAsync(recordId, 1, 0, ReviewDecisionKind.Reject, reason: ReviewReasonCode.Duplicate);
        var second = await PendingCandidateAsync(Updated.AddDays(1));

        await SettleBypassedAsync(second, VoteDraftErrorCodes.Unchanged, VoteDraftErrorCodes.RecordRejected);

        await AssertBlockAsync(second);
        await Assert.ThrowsAsync<StaleApprovalException>(() => DecideAsync(recordId, 1, 1, ReviewDecisionKind.Approve));
    }

    [Fact]
    public async Task ARefusalOfTheCandidateOfTheRevisionItselfSetsNoBlockBecauseItIsNoNewerUpdate()
    {
        var first = await PendingCandidateAsync();
        await SettleForReviewAsync(first, await RevisionAsync(first, 1));
        var recordId = await RecordIdAsync(first);
        await DecideAsync(recordId, 1, 0, ReviewDecisionKind.Reject, reason: ReviewReasonCode.Duplicate);
        await ExecuteAsync($"UPDATE vote_analysis SET status = 'Pending' WHERE candidate_id = '{first}'");

        await SettleBypassedAsync(first, VoteDraftErrorCodes.Unchanged, VoteDraftErrorCodes.RecordRejected);

        await AssertBlockAsync(null);
    }

    [Fact]
    public async Task ARefusalThatMeetsANewerUpdateAtTheSettlementIsSupersededWithNoRunAndNoBlockBecauseTheNewerUpdateDecides()
    {
        var (_, second, _) = await RejectWithTwoNewerUpdatesAsync();

        await SettleBypassedAsync(second, VoteDraftErrorCodes.Unchanged, VoteDraftErrorCodes.RecordRejected);

        await AssertBlockAsync(null);
        Assert.Equal(0L, await ScalarAsync($"SELECT count(*) FROM vote_analysis_runs WHERE candidate_id = '{second}'"));
        Assert.Equal(VoteDraftErrorCodes.SupersededByNewerCandidate, await ScalarAsync($"SELECT error_code FROM vote_analysis WHERE candidate_id = '{second}'"));
    }

    [Fact]
    public async Task ALateRefusalOfAnOlderUpdateLeavesTheBlockOfTheNewerOneBecauseTheNewestRefusedUpdateMustStayNamed()
    {
        var (_, second, third) = await RejectWithTwoNewerUpdatesAsync();
        await SettleBypassedAsync(third, VoteDraftErrorCodes.Unchanged, VoteDraftErrorCodes.RecordRejected);
        await AssertBlockAsync(third);

        await SettleBypassedAsync(second, VoteDraftErrorCodes.Unchanged, VoteDraftErrorCodes.RecordRejected);

        await AssertBlockAsync(third);
    }

    [Fact]
    public async Task ALateUnchangedSettlementOfAnOlderUpdateKeepsTheBlockBecauseOnlyAnUpdateNotOlderThanTheRefusedOneMayEndIt()
    {
        var (_, second, third) = await RejectWithTwoNewerUpdatesAsync();
        await SettleBypassedAsync(third, VoteDraftErrorCodes.Unchanged, VoteDraftErrorCodes.RecordRejected);

        await SettleBypassedAsync(second, VoteDraftErrorCodes.Unchanged, VoteDraftErrorCodes.Unchanged);

        await AssertBlockAsync(third);
    }

    [Fact]
    public async Task AnOlderLateUpdateNeverSetsTheBlockBecauseTheRevisionAlreadyHoldsNewerFacts()
    {
        var first = await PendingCandidateAsync(Updated.AddDays(1));
        await SettleForReviewAsync(first, await RevisionAsync(first, 1));
        var recordId = await RecordIdAsync(first);
        await DecideAsync(recordId, 1, 0, ReviewDecisionKind.Reject, reason: ReviewReasonCode.Duplicate);
        var older = await PendingCandidateAsync(Updated);
        var claim = (await VoteStore().TryClaimAsync(older, Lease, default))!;

        Assert.True(await VoteStore().CheckpointAsync(AnalysisCheckpoint.From(claim, AnalysisState.Succeeded, VoteDraftErrorCodes.Unchanged),
            Bypassed(older, VoteDraftErrorCodes.RecordRejected), default));

        await AssertBlockAsync(null);
    }

    [Fact]
    public async Task AnUpdateWithTheSameFactsEndsTheBlockBecauseNoFactWasRefused()
    {
        var (recordId, _) = await RejectAndRefuseANewerUpdateAsync();
        var third = await PendingCandidateAsync(Updated.AddDays(2));
        await BypassAsync(third, VoteDraftErrorCodes.Unchanged);

        await AssertBlockAsync(null);
        await DecideAsync(recordId, 1, 1, ReviewDecisionKind.Approve);

        Assert.Equal("Approved", await ScalarAsync($"SELECT review_status FROM vote_drafts WHERE record_id = '{recordId}'"));
    }

    [Fact]
    public async Task ABlockIsNotWrittenWhenThePersonChangedTheRecordBeforeTheSettlementBecauseTheWorkerMayDraftIt()
    {
        var first = await PendingCandidateAsync();
        await SettleForReviewAsync(first, await RevisionAsync(first, 1));
        var recordId = await RecordIdAsync(first);
        await DecideAsync(recordId, 1, 0, ReviewDecisionKind.Reject, reason: ReviewReasonCode.Duplicate);
        var second = await PendingCandidateAsync(Updated.AddDays(1));
        var store = VoteStore();
        var claim = (await store.TryClaimAsync(second, Lease, default))!;
        await DecideAsync(recordId, 1, 1, ReviewDecisionKind.RequestChanges, "Look again.");

        Assert.True(await store.CheckpointAsync(AnalysisCheckpoint.From(claim, AnalysisState.Succeeded, VoteDraftErrorCodes.Unchanged),
            new VoteDraftRun(BypassedRun(Guid.NewGuid().ToString("N"), second), VoteDraftErrorCodes.RecordRejected), default));

        Assert.Equal(0L, await ScalarAsync($"SELECT count(*) FROM vote_analysis_runs WHERE candidate_id = '{second}'"));
        await AssertBlockAsync(null);
        Assert.Equal("RetryWaiting", await ScalarAsync($"SELECT status FROM vote_analysis WHERE candidate_id = '{second}'"));
    }

    [Fact]
    public async Task TheRequeueCommandSendsOnlyTheRefusedVoteBackAndARealNewDraftClearsTheBlockSoTheCountReachesZero()
    {
        var first = await PendingCandidateAsync();
        await SettleForReviewAsync(first, await RevisionAsync(first, 1));
        var recordId = await RecordIdAsync(first);
        await DecideAsync(recordId, 1, 0, ReviewDecisionKind.Reject, reason: ReviewReasonCode.Duplicate);
        var second = await PendingCandidateAsync(Updated.AddDays(1));
        await SettleAsync(second, await RevisionAsync(second, 2));
        await DecideAsync(recordId, 1, 1, ReviewDecisionKind.RequestChanges, "Look again.");
        var owner = new ReviewActor("auth0|owner", ReviewRole.Owner);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new RequeueRefusedVoteDrafts(VoteStore()).ExecuteAsync(Reviewer));
        Assert.Equal(1, await new RequeueRefusedVoteDrafts(VoteStore()).ExecuteAsync(owner));
        await SettleAsync(second, await RevisionAsync(second, 2));

        await AssertBlockAsync(null);
        Assert.Equal(0, await new RequeueRefusedVoteDrafts(VoteStore()).ExecuteAsync(owner));
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
        await SettleForReviewAsync(candidate, await RevisionAsync(candidate, 1));
        var recordId = await RecordIdAsync(candidate);
        await ExecuteAsync($"SET session_replication_role = replica; UPDATE vote_revision_facts SET citation_extraction_id = '{new string('c', 64)}'");

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
            VoteReviews().SaveRevisionAsync(Reviewer.Subject, forged, 5, null, "save-6", new string('6', 64), [], default));
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
        await ExecuteAsync("UPDATE vote_revisions SET issue_candidates_recorded = false");
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
        var suggestionRunId = Guid.NewGuid().ToString("N");
        await InsertRunAsync(first, VoteDraftTask.PromptVersion, 1, runId: suggestionRunId);
        var text = ReviewText.AfterModelDraft(edited, "Model headline", "Model summary.", suggestionRunId, new string('d', 64));
        await SettleForReviewAsync(second, (await RevisionAsync(second, 3)) with
        {
            Headline = text.Headline,
            Summary = text.Summary,
            TextOrigin = text.Origin,
            Suggestion = text.Suggestion
        });
        return recordId;
    }

    private Task<IReadOnlyList<StoredDecision>> VoteDecisionsAsync() => ReviewDecisionRows.ReadAsync(connectionString, "vote_review_decisions");

    private PostgresVoteReviewStore VoteReviews() => new(new PooledDbContextFactory<CollectionAttemptDbContext>(
        new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options));

    private Task<ReviewDecision> DecideAsync(string recordId, int revision, int stateVersion, ReviewDecisionKind kind, string? note = null,
        ReviewReasonCode? reason = null) =>
        new DecideVoteReview(VoteReviews()).ExecuteAsync(Reviewer,
            new(recordId, revision, stateVersion, kind, note, [], Guid.NewGuid().ToString("N"), ReasonCode: reason));

    private Task<VoteDraftRevision> SaveAsync(string recordId, int expected, SuggestionChoice suggestion, string key,
        string headline = "Person headline", string summary = "Person summary.") =>
        new SaveVoteDraft(VoteReviews()).ExecuteAsync(Reviewer, new(recordId, expected, headline, summary, suggestion, key));
}
