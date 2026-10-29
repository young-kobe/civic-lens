using CivicLens.Application.Collection;
using CivicLens.Application.Collection.VoteviewVotes;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Collection.Jobs;
using CivicLens.Infrastructure.Documents;
using CivicLens.Core.Documents;
using CivicLens.Tests.Fixtures;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.Collection.VoteviewVotes;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PostgresVoteviewVoteTests(PostgresCollection postgres) : IAsyncLifetime
{
    private const string TestJobKeyPrefix = "test-job/";
    private static readonly DateTimeOffset FirstCapture = new(2026, 10, 28, 3, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset SecondCapture = FirstCapture.AddDays(1);
    private readonly string captureRoot = Path.Combine(Path.GetTempPath(), "voteview-vote-" + Guid.NewGuid().ToString("N"));
    private string connectionString = null!;
    private PostgresCollectionJobStore store = null!;
    private PostgresDocumentExtractionStore documents = null!;
    private JobExtractionSeeder seeder = null!;
    private ConfiguredCollectionSource source = null!;

    public async Task InitializeAsync()
    {
        connectionString = await postgres.CreateMigratedDatabaseAsync();
        store = PostgresCollectionJobStore.FromConnectionString(connectionString);
        documents = PostgresDocumentExtractionStore.FromConnectionString(connectionString);
        Directory.CreateDirectory(captureRoot);
        var candidates = new StatementCandidateSeeder(PostgresCollectionAttemptStore.FromConnectionString(connectionString), documents,
            connectionString, captureRoot);
        seeder = new JobExtractionSeeder(store, documents, candidates, connectionString, captureRoot);
        source = new ConfiguredCollectionSource(VoteviewVoteFixtures.Configuration(), VoteviewVoteFixtures.SourceId, new DateOnly(2026, 10, 29));
    }

    public async Task DisposeAsync()
    {
        await postgres.DropDatabaseAsync(connectionString);
        Directory.Delete(captureRoot, recursive: true);
    }

    [Fact]
    public async Task TheRollCallsJobAdmitsTheMembersJobAndTheMembersJobAdmitsTheVotesJobEachKeyedByItsParentSoAReplayAddsNothing()
    {
        var rollCalls = await SucceededJobs.RootAsync(store, connectionString, source, TestJobKeyPrefix + "root");

        var members = Assert.Single((await store.AdmitVoteviewFollowOnsAsync(source, ApiResource.VoteviewRollCalls, rollCalls, 119, default)).Jobs);
        await SucceededJobs.MarkSucceededAsync(connectionString, members.JobId);
        var votes = Assert.Single((await store.AdmitVoteviewFollowOnsAsync(source, ApiResource.VoteviewMembers, members.JobId, 119, default)).Jobs);
        var replay = await store.AdmitVoteviewFollowOnsAsync(source, ApiResource.VoteviewMembers, members.JobId, 119, default);

        Assert.Equal(VoteviewUrl.File(VoteviewVoteFixtures.SourceUrl, ApiResource.VoteviewMembers, 119), members.Url);
        Assert.Equal(VoteviewUrl.File(VoteviewVoteFixtures.SourceUrl, ApiResource.VoteviewVotes, 119), votes.Url);
        Assert.Empty(replay.Jobs);
        Assert.Equal(1, replay.DuplicateCount);
        Assert.Equal(VoteviewJobKeys.Votes(VoteviewVoteFixtures.SourceId, members.JobId),
            await ScalarAsync($"SELECT idempotency_key FROM collection_jobs WHERE job_id = '{votes.JobId}'"));
    }

    [Fact]
    public async Task AnEarlierCongressAlsoAdmitsTheRollCallsJobOfTheNextCongressAndTheCurrentCongressDoesNot()
    {
        var earlier = await SucceededJobs.RootAsync(store, connectionString, source, TestJobKeyPrefix + "root");

        var jobs = (await store.AdmitVoteviewFollowOnsAsync(source, ApiResource.VoteviewRollCalls, earlier, 118, default)).Jobs;

        Assert.Equal([VoteviewUrl.File(VoteviewVoteFixtures.SourceUrl, ApiResource.VoteviewMembers, 118),
            VoteviewUrl.File(VoteviewVoteFixtures.SourceUrl, ApiResource.VoteviewRollCalls, 119)], jobs.Select(job => job.Url));
        Assert.Equal(VoteviewJobKeys.RollCalls(VoteviewVoteFixtures.SourceId, earlier), await ScalarAsync(
            "SELECT idempotency_key FROM collection_jobs WHERE is_root AND idempotency_key LIKE 'voteview-senate/%/rollcalls/%'"));
    }

    [Fact]
    public async Task EachDailySweepWalksTheWholeChainAgainSoTheCurrentCongressIsFetchedOnTheSecondDay()
    {
        var dayOne = await WalkAsync("day-one", first: 117);
        var dayTwo = await WalkAsync("day-two", first: 117);

        var current = VoteviewJobKeys.CurrentCongress(DateOnly.FromDateTime(DateTime.UtcNow));
        Assert.Equal(Enumerable.Range(117, current - 116),
            dayTwo.Where(job => job.Resource == ApiResource.VoteviewVotes).Select(job => job.Congress));
        Assert.Empty(dayOne.Select(job => job.JobId).Intersect(dayTwo.Select(job => job.JobId)));
        Assert.Equal(2, dayOne.Concat(dayTwo).Count(job => job is { Resource: ApiResource.VoteviewVotes, Congress: 119 }));
    }

    [Fact]
    public async Task AFailedMiddleJobStopsThatDaysChainButTheNextDayMakesNewKeysAndReachesTheCurrentCongress()
    {
        var dayOne = await WalkAsync("failed-day-one", first: 117, failAtCongress: 118);
        var dayTwo = await WalkAsync("failed-day-two", first: 117);

        Assert.DoesNotContain(dayOne, job => job.Congress == 119);
        Assert.Contains(dayTwo, job => job is { Resource: ApiResource.VoteviewVotes, Congress: 119 });
    }

    [Fact]
    public async Task OneRollCallsJobHoldsManyVersionsAndOnlyTheRollCallWithNoBillIsQueuedBecauseABillRollCallWaitsForItsBillDetail()
    {
        var chain = await ChainAsync("one");

        var changed = await AdmitAsync(chain, FirstCapture);

        Assert.Equal([1, 60], changed.Select(roll => roll.RollCall.Part));
        Assert.Equal(2L, await ScalarAsync("SELECT count(*) FROM vote_versions"));
        Assert.Equal(2L, await ScalarAsync("SELECT count(*) FROM vote_candidates"));
        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM vote_analysis WHERE status = 'Pending'"));
    }

    [Fact]
    public async Task ACandidateNamesTheRollCallsJobAsDetailTheVotesJobAsMembersAndTheMembersJobAsRosterWithOneTextForBothPages()
    {
        var chain = await ChainAsync("two");

        var changed = await AdmitAsync(chain, FirstCapture);

        var id = changed.Single(roll => roll.RollCall.Part == 1).Extraction.ExtractionId;
        Assert.Equal($"{chain.RollCallsJobId}|{chain.VotesJobId}|{chain.MembersJobId}|{id}|{id}", await ScalarAsync(
            "SELECT detail_job_id || '|' || members_job_id || '|' || roster_job_id || '|' || detail_extraction_id || '|' || members_extraction_id " +
            "FROM vote_candidates WHERE roll_call_number = 1"));
        Assert.Equal(FirstCapture.UtcTicks, await ScalarAsync("SELECT update_date_utc_ticks FROM vote_versions WHERE roll_call_number = 1"));
    }

    [Fact]
    public async Task ANextDayWithTheSameTextWritesNoVersionButChangedTextWritesOneAndKeepsTheOldVersion()
    {
        await AdmitAsync(await ChainAsync("day-one"), FirstCapture);

        var unchanged = await AdmitAsync(await ChainAsync("day-two"), SecondCapture);
        var changed = await AdmitAsync(await ChainAsync("day-three"), SecondCapture.AddDays(1),
            new Dictionary<int, string> { [60] = "\nMember: X000000, X, R, TX, Yea" });

        Assert.Empty(unchanged);
        Assert.Equal([60], changed.Select(roll => roll.RollCall.Part));
        Assert.Equal(3L, await ScalarAsync("SELECT count(*) FROM vote_versions"));
        Assert.Equal(2L, await ScalarAsync("SELECT count(*) FROM vote_versions WHERE roll_call_number = 60"));
    }

    [Fact]
    public async Task AReplayOfTheSameVotesJobAdmitsNothingBecauseTheNewestVersionAlreadyHoldsTheText()
    {
        var chain = await ChainAsync("replay");
        var rolls = await RollsAsync(chain, FirstCapture);
        var changed = await store.SelectChangedVoteviewRollsAsync(source, 119, rolls, default);
        foreach (var roll in changed) _ = await documents.SaveAsync(roll.Extraction, default);
        await store.AdmitVoteviewVotesAsync(source, chain, FirstCapture, changed, default);

        var replay = await store.AdmitVoteviewVotesAsync(source, chain, FirstCapture, changed, default);

        Assert.Equal(2, replay.DuplicateCount);
        Assert.Equal(2L, await ScalarAsync("SELECT count(*) FROM vote_candidates"));
    }

    [Fact]
    public async Task ExtractionsFromTwoCapturesAreRefusedBecauseOneVotesJobHasOneCaptureAndTheOwnershipCheckRunsOnce()
    {
        var chain = await ChainAsync("two-captures");
        var first = await RollsAsync(chain, FirstCapture);
        var second = await RollsAsync(chain, SecondCapture);
        foreach (var roll in first.Concat(second)) _ = await documents.SaveAsync(roll.Extraction, default);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            store.AdmitVoteviewVotesAsync(source, chain, FirstCapture, [first[0], second[1]], default));

        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM vote_versions"));
    }

    [Fact]
    public async Task OnlyTheSameThreeBodiesAsTheNewestCandidateCountAsUnchanged()
    {
        var chain = await ChainAsync("bodies", rollCallsBody: "rollcalls-a", membersBody: "members-a", votesBody: "votes-a");
        await AdmitAsync(chain, FirstCapture);

        var same = await store.HasVoteviewCandidateOfBodiesAsync(source, 119, Bodies("rollcalls-a", "members-a", "votes-a"), default);
        var anyChanged = await store.HasVoteviewCandidateOfBodiesAsync(source, 119, Bodies("rollcalls-a", "members-b", "votes-a"), default);

        Assert.True(same);
        Assert.False(anyChanged);
    }

    [Fact]
    public async Task ANewExtractionVersionRendersAgainEvenWhenTheThreeBodiesAreTheSameSoARendererFixReachesOldRollCalls()
    {
        var chain = await ChainAsync("version", rollCallsBody: "rollcalls-a", membersBody: "members-a", votesBody: "votes-a");
        await AdmitAsync(chain, FirstCapture);

        var bumped = await store.HasVoteviewCandidateOfBodiesAsync(source, 119,
            Bodies("rollcalls-a", "members-a", "votes-a", VoteviewParser.ExtractionVersion + "-next"), default);

        Assert.False(bumped);
    }

    private async Task<List<(ApiResource Resource, int Congress, string JobId)>> WalkAsync(string rootKey, int first, int? failAtCongress = null)
    {
        var walked = new List<(ApiResource Resource, int Congress, string JobId)>();
        var rollCalls = await SucceededJobs.RootAsync(store, connectionString, source, TestJobKeyPrefix + rootKey);
        for (var congress = first; ; congress++)
        {
            var jobs = (await store.AdmitVoteviewFollowOnsAsync(source, ApiResource.VoteviewRollCalls, rollCalls, congress, default)).Jobs;
            var members = jobs.Single(job => job.Url.Contains("/members/"));
            await SucceededJobs.MarkSucceededAsync(connectionString, members.JobId);
            var votes = Assert.Single((await store.AdmitVoteviewFollowOnsAsync(source, ApiResource.VoteviewMembers, members.JobId, congress, default)).Jobs);
            walked.Add((ApiResource.VoteviewMembers, congress, members.JobId));
            walked.Add((ApiResource.VoteviewVotes, congress, votes.JobId));
            var next = jobs.SingleOrDefault(job => job.Url.Contains("/rollcalls/"));
            if (next is null) return walked;
            if (failAtCongress == congress + 1)
            {
                _ = await ScalarAsync($"UPDATE collection_jobs SET state = 'Failed' WHERE job_id = '{next.JobId}'");
                return walked;
            }
            await SucceededJobs.MarkSucceededAsync(connectionString, next.JobId);
            rollCalls = next.JobId;
        }
    }

    private static VoteviewBodies Bodies(string rollCalls, string members, string votes, string? version = null) =>
        new(Sha256(rollCalls), Sha256(members), Sha256(votes), version ?? VoteviewParser.ExtractionVersion);

    private static string Sha256(string text) =>
        Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)));

    private async Task<VoteviewChain> ChainAsync(string name, string rollCallsBody = "rollcalls", string membersBody = "members",
        string votesBody = "votes")
    {
        var rollCalls = await SucceededJobs.RootAsync(store, connectionString, source, TestJobKeyPrefix + name);
        _ = await seeder.AttachCaptureAsync(rollCalls, rollCallsBody);
        var members = Assert.Single((await store.AdmitVoteviewFollowOnsAsync(source, ApiResource.VoteviewRollCalls, rollCalls, 119, default)).Jobs);
        _ = await seeder.AttachCaptureAsync(members.JobId, membersBody);
        await SucceededJobs.MarkSucceededAsync(connectionString, members.JobId);
        var votes = Assert.Single((await store.AdmitVoteviewFollowOnsAsync(source, ApiResource.VoteviewMembers, members.JobId, 119, default)).Jobs);
        await SucceededJobs.MarkSucceededAsync(connectionString, votes.JobId);
        _ = await seeder.AttachCaptureAsync(votes.JobId, votesBody);
        return new VoteviewChain(119, rollCalls, members.JobId, votes.JobId);
    }

    private async Task<IReadOnlyList<VoteviewRollExtraction>> AdmitAsync(VoteviewChain chain, DateTimeOffset capturedAt,
        Dictionary<int, string>? edits = null)
    {
        var changed = await store.SelectChangedVoteviewRollsAsync(source, 119, await RollsAsync(chain, capturedAt, edits), default);
        foreach (var roll in changed) _ = await documents.SaveAsync(roll.Extraction, default);
        await store.AdmitVoteviewVotesAsync(source, chain, capturedAt, changed, default);
        return changed;
    }

    private async Task<IReadOnlyList<VoteviewRollExtraction>> RollsAsync(VoteviewChain chain, DateTimeOffset capturedAt,
        Dictionary<int, string>? edits = null)
    {
        var captured = await seeder.AttachCaptureAsync(chain.VotesJobId, "votes-" + capturedAt.Ticks, capturedAt);
        var joined = VoteviewRoll.Join(
            VoteviewParser.ParseRollCalls(VoteviewVoteFixtures.Bytes(VoteviewVoteFixtures.DefaultRollCalls), 119),
            VoteviewParser.ParseCasts(VoteviewVoteFixtures.Bytes(VoteviewVoteFixtures.DefaultVotes), 119),
            VoteviewParser.ParseMembers(VoteviewVoteFixtures.Bytes(VoteviewVoteFixtures.DefaultMembers), 119));
        return [.. joined.Select(roll => new VoteviewRollExtraction(roll.RollCall,
            new DocumentExtraction(captured, VoteviewParser.ExtractionVersion,
                VoteviewRenderer.Render(roll) + (edits?.GetValueOrDefault(roll.RollCall.Part) ?? ""), part: roll.RollCall.Part)))];
    }

    private async Task<object?> ScalarAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return await command.ExecuteScalarAsync();
    }
}
