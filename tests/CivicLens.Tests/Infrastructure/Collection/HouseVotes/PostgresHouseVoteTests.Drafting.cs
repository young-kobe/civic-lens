using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CivicLens.Application.Analysis;
using CivicLens.Application.Votes;
using CivicLens.Core.Analysis;
using CivicLens.Core.Documents;
using CivicLens.Core.Review;
using CivicLens.Core.Votes;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Votes;
using CivicLens.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.Collection.HouseVotes;

public sealed partial class PostgresHouseVoteTests
{
    private static readonly TimeSpan Lease = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task TheCandidateWriteQueuesOnePendingDraftRowInTheSameTransactionAndReplayKeepsIt()
    {
        var vote = HouseVoteFixtures.Vote(100, Updated);
        var (_, members) = await DetailWithMembersJobAsync(vote);
        await AdmitBillDetailAsync();
        Assert.Empty(await QueueAsync());

        await AdmitMembersAsync(members, vote);
        await AdmitMembersAsync(members, vote);

        var candidate = (await CandidateAsync()).CandidateId;
        Assert.Equal([(candidate, "vote-draft", "Pending", null)], await QueueAsync());
    }

    [Fact]
    public async Task EvidenceHoldsTheTwoSavedPagesTheCandidateIdentityAndTheRecordIdWithoutTheUpdateDate()
    {
        var candidate = await PendingCandidateAsync();

        var evidence = (await VoteStore().ReadEvidenceAsync(candidate, default))!;

        var row = await CandidateAsync();
        Assert.Equal((row.DetailJobId, row.MembersJobId), (evidence.Detail.JobId, evidence.Members.JobId));
        Assert.StartsWith("House vote: 119 1 100", evidence.Detail.Text, StringComparison.Ordinal);
        Assert.Equal("extraction", evidence.Members.ExtractionVersion);
        Assert.Equal((candidate, 119, 1, 100, "hconres", 14, Updated), (evidence.Candidate.CandidateId, evidence.Candidate.Congress,
            evidence.Candidate.SessionNumber, evidence.Candidate.RollCallNumber, evidence.Candidate.BillType, evidence.Candidate.BillNumber,
            evidence.Candidate.UpdateDate));
        var frame = $"house-vote-record:{HouseVoteFixtures.SourceId.Length}:{HouseVoteFixtures.SourceId}119:1:100";
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(frame))), evidence.Candidate.RecordId);
        Assert.Equal((null, false), (evidence.Current, evidence.HasNewerCandidate));
    }

    [Fact]
    public async Task ADraftedSettlementWritesTheDraftTheRevisionTheRunAndTheBudgetInOneTransaction()
    {
        var candidate = await PendingCandidateAsync();
        var store = VoteStore();
        var claim = (await store.TryClaimAsync(candidate, Lease, default))!;
        var runId = await ReserveAsync(store, claim);
        var recordId = await RecordIdAsync(candidate);

        var settled = await store.CheckpointAsync(VoteDraftCheckpoint.From(claim, await RevisionAsync(candidate, 1)), DraftedRun(runId, candidate), default);

        Assert.True(settled);
        Assert.Equal([(candidate, "vote-draft", "Succeeded", null)], await QueueAsync());
        Assert.Equal(1, await ScalarAsync($"SELECT current_revision_number FROM vote_drafts WHERE record_id = '{recordId}'"));
        Assert.Equal(("Analysis", runId), await TupleAsync($"SELECT author_kind, run_id FROM vote_revisions WHERE record_id = '{recordId}' AND revision_number = 1"));
        Assert.Equal(1_000L, await ScalarAsync("SELECT charged_tokens FROM vote_analysis_budget WHERE task = 'vote-draft'"));
        Assert.Equal(("1", "100"), await TupleAsync($"SELECT session_number, roll_call_number FROM vote_drafts WHERE record_id = '{recordId}'"));
    }

    [Fact]
    public async Task ACollectedAgainVoteAppendsRevisionTwoToTheSameDraftBecauseTheRecordIsOnePerRollCall()
    {
        var older = await PendingCandidateAsync();
        await SettleAsync(older, await RevisionAsync(older, 1));
        var newer = await PendingCandidateAsync(Updated.AddDays(1));

        await SettleAsync(newer, await RevisionAsync(newer, 2));

        Assert.Equal(2L, await ScalarAsync("SELECT count(*) FROM vote_revisions"));
        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM vote_drafts"));
        Assert.Equal(2, await ScalarAsync("SELECT current_revision_number FROM vote_drafts"));
    }

    [Fact]
    public async Task AKindOnlyChangeSettlesItsCopyRunAndAppendsRevisionTwoBecauseTheDatabaseKnowsTheKindChangedReason()
    {
        var older = await PendingCandidateAsync();
        var first = await RevisionAsync(older, 1);
        await SettleAsync(older, first);
        var newer = await PendingCandidateAsync(Updated.AddDays(1));
        var store = VoteStore();
        var claim = (await store.TryClaimAsync(newer, Lease, default))!;
        var copy = await RevisionAsync(newer, 2) with { Kind = VoteKind.Passage, KindCitation = first.Facts[0].Citation };
        var run = BypassedRun(Guid.NewGuid().ToString("N"), newer);

        var settled = await store.CheckpointAsync(VoteDraftCheckpoint.From(claim, copy), new VoteDraftRun(run, VoteDraftErrorCodes.KindChanged), default);

        Assert.True(settled);
        Assert.Equal(1L, await ScalarAsync($"SELECT count(*) FROM vote_analysis_runs WHERE run_id = '{run.RunId}' AND reason = 'kindChanged'"));
        Assert.Equal(2, await ScalarAsync("SELECT current_revision_number FROM vote_drafts"));
        Assert.Equal(1L, await ScalarAsync($"SELECT count(*) FROM vote_revisions WHERE revision_number = 2 AND run_id = '{run.RunId}'"));
    }

    [Fact]
    public async Task ANewerCandidateThatArrivesAfterTheClaimSupersedesTheAppendSoTheOlderDraftNeverBecomesARevision()
    {
        var older = await PendingCandidateAsync();
        var store = VoteStore();
        var claim = (await store.TryClaimAsync(older, Lease, default))!;
        var runId = await ReserveAsync(store, claim);
        var revision = await RevisionAsync(older, 1);
        await PendingCandidateAsync(Updated.AddDays(1));

        Assert.True(await store.CheckpointAsync(VoteDraftCheckpoint.From(claim, revision), DraftedRun(runId, older), default));

        Assert.Equal((older, "Succeeded", "supersededByNewerCandidate"), (await QueueAsync()).Where(row => row.CandidateId == older)
            .Select(row => (row.CandidateId, row.Status, row.ErrorCode)).Single());
        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM vote_revisions"));
        Assert.Equal("DraftDiscarded", await ScalarAsync($"SELECT outcome FROM vote_analysis_runs WHERE run_id = '{runId}'"));
    }

    [Fact]
    public async Task APersonsConcurrentRevisionFailsTheExpectedVersionCheckSoTheRunRetriesWithBackoffAndAppendsNothing()
    {
        var candidate = await PendingCandidateAsync();
        var store = VoteStore();
        var claim = (await store.TryClaimAsync(candidate, Lease, default))!;
        var runId = await ReserveAsync(store, claim);
        var recordId = await RecordIdAsync(candidate);
        var revision = await RevisionAsync(candidate, 1);
        await ExecuteAsync($"""
            INSERT INTO vote_drafts (record_id, source_id, chamber, congress, session_number, roll_call_number, current_revision_number, review_state_version,
                review_status, created_at_utc_ticks, last_revised_at_utc_ticks, candidate_id)
            VALUES ('{recordId}', '{HouseVoteFixtures.SourceId}', 'House', 119, 1, 100, 1, 0, 'AwaitingReview', 0, 0, '{candidate}');
            INSERT INTO vote_revisions (record_id, revision_number, author_subject, author_kind, run_id, revision_json, created_at_utc_ticks)
            VALUES ('{recordId}', 1, 'auth0|editor', 'Person', NULL, 'x', 0)
            """);

        Assert.True(await store.CheckpointAsync(VoteDraftCheckpoint.From(claim, revision), DraftedRun(runId, candidate), default));

        var row = Assert.Single(await QueueAsync());
        Assert.Equal(("RetryWaiting", VoteDraftErrorCodes.RevisionConflict), (row.Status, row.ErrorCode));
        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM vote_revisions"));
        Assert.Equal("DraftDiscarded", await ScalarAsync($"SELECT outcome FROM vote_analysis_runs WHERE run_id = '{runId}'"));
        Assert.True((long)(await ScalarAsync("SELECT retry_at FROM vote_analysis"))! > 0);
    }

    [Fact]
    public async Task AnAppendWaitsForTheVoteReviewLockSoAHumanSaveAndTheWorkerNeverInterleave()
    {
        var candidate = await PendingCandidateAsync();
        var store = VoteStore();
        var claim = (await store.TryClaimAsync(candidate, Lease, default))!;
        var runId = await ReserveAsync(store, claim);
        var revision = await RevisionAsync(candidate, 1);
        Task<bool> settling;
        await using (var holder = await HeldAdvisoryLock.AcquireAsync(connectionString, "civic-lens-vote-review"))
        {
            settling = store.CheckpointAsync(VoteDraftCheckpoint.From(claim, revision), DraftedRun(runId, candidate), default);
            await HeldAdvisoryLock.WaitForWaiterAsync(connectionString, "civic-lens-vote-review");
            Assert.False(settling.IsCompleted);
        }

        Assert.True(await settling);
    }

    [Fact]
    public async Task AnUnchangedOrNoMemberSettlementWithoutItsRunIsRefused()
    {
        var candidate = await PendingCandidateAsync();
        var store = VoteStore();
        var claim = (await store.TryClaimAsync(candidate, Lease, default))!;

        foreach (var code in new[] { VoteDraftErrorCodes.Unchanged, VoteDraftErrorCodes.NoTrackedMember })
            await Assert.ThrowsAsync<ArgumentException>(() => store.CheckpointAsync(new AnalysisCheckpoint(candidate, VoteDraftTask.Task,
                AnalysisState.Running, claim.LeaseToken, claim.Fence, AnalysisState.Succeeded, code), null, default));
    }

    [Fact]
    public async Task TheDatabaseRefusesAnAnalysisRevisionWithoutARunAndAPersonRevisionWithOne()
    {
        var candidate = await PendingCandidateAsync();
        var recordId = await RecordIdAsync(candidate);
        await ExecuteAsync($"""
            INSERT INTO vote_drafts (record_id, source_id, chamber, congress, session_number, roll_call_number, current_revision_number, review_state_version,
                review_status, created_at_utc_ticks, last_revised_at_utc_ticks, candidate_id)
            VALUES ('{recordId}', '{HouseVoteFixtures.SourceId}', 'House', 119, 1, 100, 1, 0, 'AwaitingReview', 0, 0, '{candidate}')
            """);

        var withoutRun = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync($"""
            INSERT INTO vote_revisions (record_id, revision_number, author_subject, author_kind, run_id, revision_json, created_at_utc_ticks)
            VALUES ('{recordId}', 1, 'civic-lens:analysis', 'Analysis', NULL, 'x', 0)
            """));
        var personWithRun = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync($"""
            INSERT INTO vote_revisions (record_id, revision_number, author_subject, author_kind, run_id, revision_json, created_at_utc_ticks)
            VALUES ('{recordId}', 1, 'auth0|editor', 'Person', '{new string('1', 32)}', 'x', 0)
            """));

        Assert.Equal([PostgresErrorCodes.CheckViolation, PostgresErrorCodes.CheckViolation], [withoutRun.SqlState, personWithRun.SqlState]);
    }

    [Fact]
    public async Task TheDatabaseAllowsOneDraftForEachRollCallAndRefusesABadRecordId()
    {
        var candidate = await PendingCandidateAsync();
        var insert = (string id) => $"""
            INSERT INTO vote_drafts (record_id, source_id, chamber, congress, session_number, roll_call_number, current_revision_number, review_state_version,
                review_status, created_at_utc_ticks, last_revised_at_utc_ticks, candidate_id)
            VALUES ('{id}', 'source', 'House', 119, 1, 100, 1, 0, 'AwaitingReview', 0, 0, '{candidate}')
            """;
        await ExecuteAsync(insert(new string('a', 64)));

        var duplicate = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(insert(new string('b', 64))));
        var badId = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(insert("not-hex")));

        Assert.Equal([PostgresErrorCodes.UniqueViolation, PostgresErrorCodes.CheckViolation], [duplicate.SqlState, badId.SqlState]);
    }

    [Fact]
    public async Task RequeueBlockedMovesOnlyBlockedVotesWithTheGivenCodeAndResetsTheirAttempts()
    {
        var requeued = await PendingCandidateAsync();
        var otherCode = await PendingCandidateAsync(Updated.AddDays(1));
        var failed = await PendingCandidateAsync(Updated.AddDays(2));
        await ExecuteAsync($"""
            UPDATE vote_analysis SET attempts = 2, status = CASE candidate_id WHEN '{failed}' THEN 'Failed' ELSE 'Blocked' END,
                error_code = CASE candidate_id WHEN '{otherCode}' THEN '{VoteDraftErrorCodes.OverByteCap}' ELSE '{VoteDraftErrorCodes.FactTooLong}' END
            """);

        Assert.Equal(1, await VoteStore().RequeueBlockedAsync(VoteDraftErrorCodes.FactTooLong, default));

        var queue = (await QueueAsync()).ToDictionary(row => row.CandidateId, row => (row.Status, row.ErrorCode));
        Assert.Equal(("Pending", (string?)null), queue[requeued]);
        Assert.Equal(0, await ScalarAsync($"SELECT attempts FROM vote_analysis WHERE candidate_id = '{requeued}'"));
        Assert.Equal(("Blocked", VoteDraftErrorCodes.OverByteCap), queue[otherCode]);
        Assert.Equal(("Failed", VoteDraftErrorCodes.FactTooLong), queue[failed]);
    }

    [Fact]
    public async Task ListAndCountSplitTheQueueIntoTheSameViewsSoTheOwnerSeesWhereEveryVoteStands()
    {
        var pending = await PendingCandidateAsync();
        var unchanged = await PendingCandidateAsync(Updated.AddDays(1));
        var failed = await PendingCandidateAsync(Updated.AddDays(2));
        await ExecuteAsync($"""
            UPDATE vote_analysis SET status = CASE candidate_id WHEN '{failed}' THEN 'Failed' WHEN '{unchanged}' THEN 'Succeeded' ELSE status END,
                error_code = CASE candidate_id WHEN '{failed}' THEN 'outputRejected' WHEN '{unchanged}' THEN '{VoteDraftErrorCodes.Unchanged}' END
            """);

        var counts = await VoteStore().CountAsync(default);
        var unchangedItems = await VoteStore().ListAsync(VoteDraftView.Unchanged, 10, default);

        Assert.Equal([1, 1, 1, 0, 0, 0], new[] { VoteDraftView.Pending, VoteDraftView.Unchanged, VoteDraftView.Failed, VoteDraftView.Drafted,
            VoteDraftView.Superseded, VoteDraftView.NoTrackedMember }.Select(view => counts[view]));
        var item = Assert.Single(unchangedItems);
        Assert.Equal((unchanged, "House 119 1 100", "119 hconres 14", VoteDraftView.Unchanged), (item.CandidateId, item.RollCall, item.Bill, item.View));
        Assert.Equal(3, (await VoteStore().ListAsync(null, 10, default)).Count);
        Assert.Contains(pending, (await VoteStore().ListAsync(VoteDraftView.Pending, 10, default)).Select(entry => entry.CandidateId));
    }

    [Fact]
    public async Task RedraftMovesASettledEntryWhoseNewestDraftRunUsedAnOlderPromptAndLeavesASupersededEntryAndResetsAttempts()
    {
        var (older, newer) = await TwoSettledCandidatesAsync(VoteDraftErrorCodes.SupersededByNewerCandidate, null);
        await InsertRunAsync(older, "vote-draft-prompt-v1", 1);
        await InsertRunAsync(newer, "vote-draft-prompt-v1", 2);

        Assert.Equal(1, await VoteStore().RequeueOlderPromptAsync(default));

        var queue = await StatusAndAttemptsAsync();
        Assert.Equal(("Succeeded", 2), queue[older]);
        Assert.Equal(("Pending", 0), queue[newer]);
    }

    [Fact]
    public async Task RedraftMovesNothingWhenTheNewestDraftRunOfTheRollCallUsedTheCurrentPrompt()
    {
        var (older, newer) = await TwoSettledCandidatesAsync(VoteDraftErrorCodes.Unchanged, null);
        await InsertRunAsync(older, "vote-draft-prompt-v1", 1);
        await InsertRunAsync(newer, VoteDraftTask.PromptVersion, 2);

        Assert.Equal(0, await VoteStore().RequeueOlderPromptAsync(default));

        Assert.All((await StatusAndAttemptsAsync()).Values, entry => Assert.Equal(("Succeeded", 2), entry));
    }

    [Fact]
    public async Task RedraftIgnoresANewerBypassedRunBecauseOnlyTheNewestDraftedRunNamesThePromptTheDraftCameFrom()
    {
        var (older, newer) = await TwoSettledCandidatesAsync(VoteDraftErrorCodes.SupersededByNewerCandidate, VoteDraftErrorCodes.Unchanged);
        await InsertRunAsync(newer, "vote-draft-prompt-v1", 1);
        await InsertRunAsync(newer, VoteDraftTask.PromptVersion, 2, "Bypassed");

        Assert.Equal(1, await VoteStore().RequeueOlderPromptAsync(default));

        var queue = (await QueueAsync()).ToDictionary(row => row.CandidateId, row => row.Status);
        Assert.Equal("Pending", queue[newer]);
        Assert.Equal("Succeeded", queue[older]);
    }

    [Theory]
    [InlineData("Succeeded", VoteDraftErrorCodes.NoTrackedMember)]
    [InlineData("Failed", "outputRejected")]
    [InlineData("Blocked", VoteDraftErrorCodes.FactTooLong)]
    public async Task RedraftLeavesAnEntryThatIsNotADraftToReplaceEvenWhenItsRunUsedAnOlderPrompt(string status, string errorCode)
    {
        var candidate = await PendingCandidateAsync();
        await ExecuteAsync($"UPDATE vote_analysis SET status = '{status}', error_code = '{errorCode}', attempts = 2");
        await InsertRunAsync(candidate, "vote-draft-prompt-v1", 1);

        Assert.Equal(0, await VoteStore().RequeueOlderPromptAsync(default));

        Assert.Equal((status, 2), Assert.Single((await StatusAndAttemptsAsync()).Values));
    }

    [Theory]
    [InlineData("vote-draft-prompt-v1", true)]
    [InlineData(VoteDraftTask.PromptVersion, false)]
    public async Task TheEvidenceNamesAnOlderPromptOnlyWhenTheNewestDraftRunUsedOneSoARerunSpendsNothingAfterTheRedraft(string prompt, bool older)
    {
        var candidate = await PendingCandidateAsync();
        await InsertRunAsync(candidate, prompt, 1);

        Assert.Equal(older, (await VoteStore().ReadEvidenceAsync(candidate, default))!.DraftedUnderOlderPrompt);
    }

    [Fact]
    public async Task AVoteThatAWorkerSettledUnderTheCurrentPromptHasNoOlderPromptSoTheRedraftSettlesItForGood()
    {
        var candidate = await PendingCandidateAsync();
        await SettleAsync(candidate, await RevisionAsync(candidate, 1));

        Assert.False((await VoteStore().ReadEvidenceAsync(candidate, default))!.DraftedUnderOlderPrompt);
        Assert.Equal(0, await VoteStore().RequeueOlderPromptAsync(default));
    }

    [Theory]
    [InlineData("none", 1)]
    [InlineData("person", 0)]
    [InlineData("AwaitingReviewWithConcerns", 0)]
    [InlineData("ChangesRequested", 0)]
    [InlineData("ApprovalWithdrawn", 0)]
    public async Task ARedraftSkipsAVoteWhoseCurrentRevisionHoldsPersonTextAnOpenConcernOrAWithdrawnApproval(string change, int requeued)
    {
        var candidate = await PendingCandidateAsync();
        var revision = await RevisionAsync(candidate, 1);
        await SettleAsync(candidate, revision);
        await ExecuteAsync("UPDATE vote_analysis_runs SET prompt_version = 'vote-draft-prompt-v1'");
        if (change == "person")
            await ExecuteAsync($"UPDATE vote_revisions SET revision_json = $json${JsonSerializer.Serialize(revision with { TextOrigin = ReviewTextOrigin.Person }, new JsonSerializerOptions(JsonSerializerDefaults.Web))}$json$");
        else if (change != "none")
            await ExecuteAsync($"UPDATE vote_drafts SET review_status = '{change}'");

        Assert.Equal(requeued == 1, (await VoteStore().ReadEvidenceAsync(candidate, default))!.DraftedUnderOlderPrompt);
        Assert.Equal(requeued, await VoteStore().RequeueOlderPromptAsync(default));
    }

    [Fact]
    public async Task TheEvidenceOfAVoteWithANewerCandidateNamesNoOlderPromptBecauseTheNewerCandidateWillDraftIt()
    {
        var older = await PendingCandidateAsync();
        await InsertRunAsync(older, "vote-draft-prompt-v1", 1);
        await PendingCandidateAsync(Updated.AddDays(1));

        Assert.False((await VoteStore().ReadEvidenceAsync(older, default))!.DraftedUnderOlderPrompt);
    }

    [Fact]
    public async Task RedraftLeavesARollCallWithoutAModelDraftedRunBecauseThereIsNoOldPromptToReplace()
    {
        var candidate = await PendingCandidateAsync();
        await ExecuteAsync($"UPDATE vote_analysis SET status = 'Succeeded', error_code = '{VoteDraftErrorCodes.Unchanged}', attempts = 2");
        await InsertRunAsync(candidate, "vote-draft-prompt-v1", 1, "Bypassed");

        Assert.Equal(0, await VoteStore().RequeueOlderPromptAsync(default));
    }

    [Fact]
    public async Task RedraftLeavesAVoteWithoutABillBecauseNoIssueNarrowingCanChangeIt()
    {
        var candidate = await PendingCandidateAsync();
        await ExecuteAsync("UPDATE vote_candidates SET bill_type = NULL, bill_number = NULL");
        await ExecuteAsync("UPDATE vote_analysis SET status = 'Succeeded', attempts = 2");
        await InsertRunAsync(candidate, "vote-draft-prompt-v1", 1);

        Assert.Equal(0, await VoteStore().RequeueOlderPromptAsync(default));
    }

    [Fact]
    public async Task RedraftTreatsEachRollCallOfOneBillAlone()
    {
        var first = await PendingCandidateAsync();
        var vote = HouseVoteFixtures.Vote(101, Updated);
        var (_, members) = await DetailWithMembersJobAsync(vote);
        await AdmitMembersAsync(members, vote);
        var second = (string)(await ScalarAsync($"SELECT candidate_id FROM vote_candidates WHERE roll_call_number = 101"))!;
        await ExecuteAsync("UPDATE vote_analysis SET status = 'Succeeded', attempts = 2");
        await InsertRunAsync(first, "vote-draft-prompt-v1", 1);
        await InsertRunAsync(second, VoteDraftTask.PromptVersion, 2);

        Assert.Equal(1, await VoteStore().RequeueOlderPromptAsync(default));

        var queue = (await QueueAsync()).ToDictionary(row => row.CandidateId, row => row.Status);
        Assert.Equal(("Pending", "Succeeded"), (queue[first], queue[second]));
    }

    [Fact]
    public async Task RedraftMovesEveryDraftedEntryOfAStaleRollCallAndCountsEntries()
    {
        var (older, newer) = await TwoSettledCandidatesAsync(null, VoteDraftErrorCodes.Unchanged);
        await InsertRunAsync(older, "vote-draft-prompt-v1", 1);
        await InsertRunAsync(newer, "vote-draft-prompt-v1", 2, "Bypassed");

        Assert.Equal(2, await VoteStore().RequeueOlderPromptAsync(default));

        Assert.All((await QueueAsync()).Select(row => row.Status), status => Assert.Equal("Pending", status));
    }

    [Theory]
    [InlineData("chamber", "Senate")]
    [InlineData("source_id", "another-source")]
    [InlineData("congress", "118")]
    [InlineData("session_number", "2")]
    public async Task RedraftTreatsAVoteThatDiffersInAnyRollCallKeyTermAsAnotherRollCall(string column, string value)
    {
        var (house, other) = await TwoSettledCandidatesAsync(null, null);
        var literal = int.TryParse(value, out _) ? value : $"'{value}'";
        await ExecuteAsync($"BEGIN; SET LOCAL session_replication_role = replica; UPDATE vote_candidates SET {column} = {literal} WHERE candidate_id = '{other}'; COMMIT;");
        await InsertRunAsync(house, "vote-draft-prompt-v1", 1);
        await InsertRunAsync(other, VoteDraftTask.PromptVersion, 2);

        Assert.Equal(1, await VoteStore().RequeueOlderPromptAsync(default));

        var queue = (await QueueAsync()).ToDictionary(row => row.CandidateId, row => row.Status);
        Assert.Equal(("Pending", "Succeeded"), (queue[house], queue[other]));
    }

    private async Task<(string Older, string Newer)> TwoSettledCandidatesAsync(string? olderCode, string? newerCode)
    {
        var older = await PendingCandidateAsync();
        var newer = await PendingCandidateAsync(Updated.AddDays(1));
        await ExecuteAsync($"""
            UPDATE vote_analysis SET status = 'Succeeded', attempts = 2,
                error_code = CASE candidate_id WHEN '{older}' THEN {SqlLiteral(olderCode)} ELSE {SqlLiteral(newerCode)} END
            """);
        return (older, newer);
    }

    private async Task<Dictionary<string, (string Status, int Attempts)>> StatusAndAttemptsAsync()
    {
        var entries = new Dictionary<string, (string, int)>();
        foreach (var row in await QueueAsync())
            entries[row.CandidateId] = (row.Status, Convert.ToInt32(await ScalarAsync($"SELECT attempts FROM vote_analysis WHERE candidate_id = '{row.CandidateId}'")));
        return entries;
    }

    private static string SqlLiteral(string? value) => value is null ? "NULL" : $"'{value}'";

    private async Task InsertRunAsync(string candidate, string promptVersion, long startedAtTicks, string outcome = "Drafted") => await ExecuteAsync($"""
        INSERT INTO vote_analysis_runs (run_id, candidate_id, task, task_version, model, prompt_version, schema_version, input_hash, outcome,
            input_tokens, output_tokens, cache_read_tokens, cache_write_tokens, charged_tokens, started_at_utc_ticks, finished_at_utc_ticks)
        VALUES ('{Guid.NewGuid():N}', '{candidate}', '{VoteDraftTask.Task}', '{VoteDraftTask.TaskVersion}', '{VoteDraftTask.Model}', '{promptVersion}',
            '{VoteDraftTask.SchemaVersion}', '{new string('1', 64)}', '{outcome}', 0, 0, 0, 0, 0, {startedAtTicks}, {startedAtTicks})
        """);

    private async Task<string> PendingCandidateAsync(DateTimeOffset? updated = null)
    {
        var vote = HouseVoteFixtures.Vote(100, updated ?? Updated);
        var (_, members) = await DetailWithMembersJobAsync(vote);
        await AdmitMembersAsync(members, vote);
        await AdmitBillDetailAsync();
        return (string)(await ScalarAsync($"SELECT candidate_id FROM vote_candidates WHERE update_date_utc_ticks = {vote.UpdateDate.UtcTicks}"))!;
    }

    private PostgresVoteDraftStore VoteStore() => new(new PooledDbContextFactory<CollectionAttemptDbContext>(
        new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options));

    private async Task<string> RecordIdAsync(string candidate) => (await VoteStore().ReadEvidenceAsync(candidate, default))!.Candidate.RecordId;

    private async Task SettleAsync(string candidate, VoteDraftRevision revision)
    {
        var store = VoteStore();
        var claim = (await store.TryClaimAsync(candidate, Lease, default))!;
        var runId = await ReserveAsync(store, claim);
        Assert.True(await store.CheckpointAsync(VoteDraftCheckpoint.From(claim, revision), DraftedRun(runId, candidate), default));
    }

    private async Task SettleForReviewAsync(string candidate, VoteDraftRevision revision)
    {
        await SettleAsync(candidate, revision);
        await ReopenAsync(revision.RecordId);
    }

    // Puts the record in the state of a worker revision that code did not approve, for tests of the person flow.
    private Task ReopenAsync(string recordId) => ExecuteAsync($"""
        WITH removed AS (
            DELETE FROM vote_review_decisions d USING vote_drafts r
             WHERE d.record_id = r.record_id AND d.record_id = '{recordId}' AND d.review_state_version = r.review_state_version
               AND d.decision_json::jsonb ->> 'actorSubject' = '{ReviewAuthor.CodeSubject}'
         RETURNING 1)
        UPDATE vote_drafts SET review_state_version = review_state_version - 1, review_status = 'AwaitingReview'
         WHERE record_id = '{recordId}' AND EXISTS (SELECT 1 FROM removed)
        """);

    private static async Task<string> ReserveAsync(PostgresVoteDraftStore store, AnalysisClaim claim)
    {
        var runId = Guid.NewGuid().ToString("N");
        Assert.Equal(AnalysisReservation.Reserved, await store.ReserveAsync(claim, runId, new string('1', 64), 5_000, 1_000_000, default));
        return runId;
    }

    private static VoteDraftRun DraftedRun(string runId, string candidate) => new(new AnalysisRun(runId, candidate, VoteDraftTask.Task,
        VoteDraftTask.TaskVersion, VoteDraftTask.Model, VoteDraftTask.PromptVersion, VoteDraftTask.SchemaVersion, new string('1', 64), null,
        AnalysisRunOutcome.Drafted, new(900, 100, 0, 0), 1_000, "end_turn", "req", "{}", [], "{}", DateTimeOffset.UnixEpoch,
        DateTimeOffset.UnixEpoch), null);

    private static AnalysisRun BypassedRun(string runId, string candidate) => new(runId, candidate, VoteDraftTask.Task,
        VoteDraftTask.TaskVersion, VoteDraftTask.Model, VoteDraftTask.PromptVersion, VoteDraftTask.SchemaVersion, new string('1', 64), null,
        AnalysisRunOutcome.Bypassed, AnalysisTokenUsage.None, 0, null, null, null, [], "{}", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);

    private async Task<VoteDraftRevision> RevisionAsync(string candidate, int number)
    {
        var evidence = (await VoteStore().ReadEvidenceAsync(candidate, default))!;
        var fact = new SourceSpan(evidence.Detail.ExtractionId, 0, 5);
        var member = new SourceSpan(evidence.Members.ExtractionId, 0, 5);
        var identity = evidence.Candidate;
        return new(identity.RecordId, number, "civic-lens:analysis", Updated.AddMinutes(number), "Headline", "Summary.", ReviewTextOrigin.Model, null,
            identity.SourceId, VoteSource.CongressGov, identity.Chamber, identity.Congress, identity.SessionNumber, identity.RollCallNumber, identity.BillType, identity.BillNumber, candidate,
            identity.UpdateDate, [new("Result: Passed", fact)], new DateOnly(2025, 4, 10), fact, VoteKind.Other, null, [new("with-id-a", "A000001", "Yea", member)], [], [], new string('e', 64));
    }

    private async Task<IReadOnlyList<(string CandidateId, string Task, string Status, string? ErrorCode)>> QueueAsync()
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT candidate_id, task, status, error_code FROM vote_analysis ORDER BY created_at_utc_ticks, candidate_id", connection);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<(string CandidateId, string Task, string Status, string? ErrorCode)>();
        while (await reader.ReadAsync())
            rows.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3)));
        return rows;
    }

    private async Task<(string, string)> TupleAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (reader.GetValue(0).ToString()!, reader.GetValue(1).ToString()!);
    }
}
