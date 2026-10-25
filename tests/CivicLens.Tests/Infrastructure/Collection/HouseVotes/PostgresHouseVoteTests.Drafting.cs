using System.Security.Cryptography;
using System.Text;
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
                review_status, created_at_utc_ticks)
            VALUES ('{recordId}', '{HouseVoteFixtures.SourceId}', 'House', 119, 1, 100, 1, 0, 'AwaitingReview', 0);
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
                review_status, created_at_utc_ticks)
            VALUES ('{recordId}', '{HouseVoteFixtures.SourceId}', 'House', 119, 1, 100, 1, 0, 'AwaitingReview', 0)
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
        var insert = (string id) => $"""
            INSERT INTO vote_drafts (record_id, source_id, chamber, congress, session_number, roll_call_number, current_revision_number, review_state_version,
                review_status, created_at_utc_ticks)
            VALUES ('{id}', 'source', 'House', 119, 1, 100, 1, 0, 'AwaitingReview', 0)
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
            identity.SourceId, identity.Chamber, identity.Congress, identity.SessionNumber, identity.RollCallNumber, identity.BillType, identity.BillNumber, candidate,
            identity.UpdateDate, [new("Result: Passed", fact)], new DateOnly(2025, 4, 10), fact, VoteKind.Other, null, [new("with-id-a", "A000001", "Yea", member)], [], new string('e', 64));
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
