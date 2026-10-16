using System.Security.Cryptography;
using System.Text;
using CivicLens.Application.Analysis;
using CivicLens.Application.Votes;
using CivicLens.Core.Analysis;
using CivicLens.Core.Documents;
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
        Assert.Equal(("parser", "normalizer"), (evidence.Members.ParserVersion, evidence.Members.NormalizationVersion));
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
            INSERT INTO vote_drafts (record_id, source_id, congress, session_number, roll_call_number, current_revision_number, review_state_version,
                review_status, created_at_utc_ticks, last_revised_at_utc_ticks)
            VALUES ('{recordId}', '{HouseVoteFixtures.SourceId}', 119, 1, 100, 1, 0, 'AwaitingReview', 0, 0);
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
            INSERT INTO vote_drafts (record_id, source_id, congress, session_number, roll_call_number, current_revision_number, review_state_version,
                review_status, created_at_utc_ticks, last_revised_at_utc_ticks)
            VALUES ('{recordId}', '{HouseVoteFixtures.SourceId}', 119, 1, 100, 1, 0, 'AwaitingReview', 0, 0)
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
            INSERT INTO vote_drafts (record_id, source_id, congress, session_number, roll_call_number, current_revision_number, review_state_version,
                review_status, created_at_utc_ticks, last_revised_at_utc_ticks)
            VALUES ('{id}', 'source', 119, 1, 100, 1, 0, 'AwaitingReview', 0, 0)
            """;
        await ExecuteAsync(insert(new string('a', 64)));

        var duplicate = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(insert(new string('b', 64))));
        var badId = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(insert("not-hex")));

        Assert.Equal([PostgresErrorCodes.UniqueViolation, PostgresErrorCodes.CheckViolation], [duplicate.SqlState, badId.SqlState]);
    }

    private async Task<string> PendingCandidateAsync(DateTimeOffset? updated = null)
    {
        var vote = HouseVoteFixtures.Vote(100, updated ?? Updated);
        var (_, members) = await DetailWithMembersJobAsync(vote);
        await AdmitMembersAsync(members, vote);
        return (string)(await ScalarAsync($"SELECT candidate_id FROM house_vote_candidates WHERE update_date_utc_ticks = {vote.UpdateDate.UtcTicks}"))!;
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

    private async Task<VoteDraftRevision> RevisionAsync(string candidate, int number)
    {
        var evidence = (await VoteStore().ReadEvidenceAsync(candidate, default))!.Candidate;
        var span = new SourceSpan(new string('c', 64), 0, 5);
        return new(evidence.RecordId, number, "civic-lens:analysis", Updated.AddMinutes(number), "Headline", "Summary.", evidence.SourceId,
            evidence.Congress, evidence.SessionNumber, evidence.RollCallNumber, evidence.BillType, evidence.BillNumber, candidate,
            evidence.UpdateDate, [new("Result: Passed", span)], [new("aderholt", "A000055", "Yea", span)]);
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
