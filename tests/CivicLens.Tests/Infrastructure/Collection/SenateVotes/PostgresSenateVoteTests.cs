using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CivicLens.Application.Collection;
using CivicLens.Application.Collection.Discovery;
using CivicLens.Application.Collection.Jobs;
using CivicLens.Application.Collection.SenateVotes;
using CivicLens.Application.Configuration;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Collection.Jobs;
using CivicLens.Infrastructure.Documents;
using CivicLens.Tests.Fixtures;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.Collection.SenateVotes;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PostgresSenateVoteTests(PostgresCollection postgres) : IAsyncLifetime
{
    private const string SourceUrl = SenateVoteFixtures.SourceUrl;
    private const string TestJobKeyPrefix = "test-job/";
    private static readonly DateOnly Backfill = SenateVoteFixtures.Backfill;
    private static readonly DateTimeOffset Modified = new(2025, 1, 10, 19, 57, 0, TimeSpan.Zero);
    private string connectionString = null!;
    private readonly string captureRoot = Path.Combine(Path.GetTempPath(), "senate-vote-" + Guid.NewGuid().ToString("N"));
    private PostgresCollectionJobStore store = null!;
    private JobExtractionSeeder extractions = null!;

    public async Task InitializeAsync()
    {
        connectionString = await postgres.CreateMigratedDatabaseAsync();
        store = PostgresCollectionJobStore.FromConnectionString(connectionString);
        var documents = PostgresDocumentExtractionStore.FromConnectionString(connectionString);
        Directory.CreateDirectory(captureRoot);
        var seeder = new StatementCandidateSeeder(PostgresCollectionAttemptStore.FromConnectionString(connectionString), documents,
            connectionString, captureRoot);
        extractions = new JobExtractionSeeder(store, documents, seeder, connectionString, captureRoot);
    }

    public async Task DisposeAsync()
    {
        await postgres.DropDatabaseAsync(connectionString);
        Directory.Delete(captureRoot, recursive: true);
    }

    [Fact]
    public async Task FirstDueCheckRequestsTheMenuOfTheSessionOfTheBackfillDateWithoutACredential()
    {
        await store.SynchronizeAsync(SenateVoteFixtures.Configuration(), default);
        await ExecuteAsync("UPDATE collection_source_schedules SET next_due_utc_ticks = 0");

        Assert.Equal(1, await store.AdvanceDueAsync(10, default));

        var job = Assert.Single(await store.ListAsync(10, default));
        Assert.Equal(SourceUrl + "/roll_call_lists/vote_menu_119_1.xml", job.Definition.Url);
        Assert.Equal(ApiResource.SenateVoteMenu, job.Definition.ApiResource);
        Assert.Null(job.Definition.Credential);
    }

    [Fact]
    public async Task MenuAdmitsOneJobForEachVoteAndAReplayFindsThemAllKnown()
    {
        var menu = Menu(Item(1, DaysAgo(200)), Item(2, DaysAgo(2)));

        var first = await AdmitMenuAsync("sweep-one", menu);
        var replay = await AdmitMenuAsync("sweep-one", menu);

        Assert.Equal(3, first.Jobs.Count);
        Assert.Contains(first.Jobs, job => job.Url == SourceUrl + "/roll_call_votes/vote1191/vote_119_1_00002.xml");
        Assert.Empty(replay.Jobs);
        Assert.Equal(3, replay.DuplicateCount);
    }

    [Fact]
    public async Task ASweepKeysAVoteOlderThan30DaysAsFinalAndARecentVoteByTheDayOfTheSweep()
    {
        await AdmitMenuAsync("sweep-one", Menu(Item(1, DaysAgo(200)), Item(2, DaysAgo(2))));

        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM collection_jobs WHERE idempotency_key LIKE 'senate-vote/%/1/final'"));
        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM collection_jobs WHERE idempotency_key ~ 'senate-vote/.*/2/[0-9]{4}-[0-9]{2}-[0-9]{2}$'"));
    }

    [Fact]
    public async Task AFullMenuWithItsNextMenuJobFitsTheBudgetButOneMoreVoteIsAnInvalidDeferralThatSavesNothing()
    {
        var votes = Enumerable.Range(1, SenateVoteMenuParser.MaximumVotes + 1).Select(roll => Item(roll, DaysAgo(200))).ToArray();

        await Assert.ThrowsAsync<InvalidDataException>(() => AdmitMenuAsync("too-many", Menu(votes)));

        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM collection_jobs WHERE idempotency_key NOT LIKE 'test-job/%'"));
    }

    [Fact]
    public async Task ShippedSourceBudgetsAdmitAFullMenuAndItsNextMenu()
    {
        var shipped = JsonSerializer.Deserialize<CollectionConfiguration>(
            File.ReadAllText(Path.Combine(RepositoryRoot.FullName, "config", "federal-coverage.json")), ConfigurationJson.Options)!;
        var source = shipped.Sources.Single(candidate => candidate.Id == SenateVoteFixtures.SourceId);
        shipped = shipped with { Sources = [source with { Enabled = true }] };
        var configured = new ConfiguredCollectionSource(shipped, SenateVoteFixtures.SourceId, Backfill.AddDays(400));
        var menu = Menu(Enumerable.Range(1, SenateVoteMenuParser.MaximumVotes).Select(roll => Item(roll, DaysAgo(200))).ToArray());
        var list = await SucceededJobs.RootAsync(store, connectionString, configured, TestJobKeyPrefix + "shipped");

        var result = await store.AdmitSenateMenuAsync(configured, list, menu, default);

        Assert.Equal(SenateVoteMenuParser.MaximumVotes + 1, result.AdmittedCount);
        Assert.Equal(0, result.DeferredCount);
    }

    [Fact]
    public async Task AVoteFileRecordsTheVersionAndOneCandidateThatNamesTheSameJobAndExtractionTwice()
    {
        var job = await EnqueueVoteAsync("vote", 1);
        var extraction = await ExtractionAsync(job);

        var result = await store.AdmitSenateVoteAsync(Source(), job, Vote(1, "s", 5), extraction, default);

        Assert.Empty(result.Jobs);
        Assert.Equal((job, "Senate", 119L, 1L, 1L, Modified.UtcTicks, "s", 5L, extraction),
            await VersionAsync());
        Assert.Equal((job, job, extraction, extraction, Source().Revision.Id), await CandidateJobsAsync());
        Assert.Matches("^[0-9a-f]{64}$", (string)(await ScalarAsync("SELECT candidate_id FROM vote_candidates"))!);
    }

    [Fact]
    public async Task SenateCandidateIdsAreTheHashOfTheirFramedTextSoTheyNeverChange()
    {
        var job = await EnqueueVoteAsync("vote", 1);

        await store.AdmitSenateVoteAsync(Source(), job, Vote(1, null, null), await ExtractionAsync(job), default);

        var id = SenateVoteFixtures.SourceId;
        var text = $"senate-vote:{id.Length}:{id}119:1:1:{Modified.UtcTicks}";
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text))), await ScalarAsync("SELECT candidate_id FROM vote_candidates"));
    }

    [Fact]
    public async Task ASenateCandidateWithoutABillIsQueuedAtOnceBecauseANominationHasNoBillTitleToWaitFor()
    {
        var job = await EnqueueVoteAsync("vote", 1);

        await store.AdmitSenateVoteAsync(Source(), job, Vote(1, null, null), await ExtractionAsync(job), default);

        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM vote_candidates"));
        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM vote_analysis WHERE status = 'Pending'"));
    }

    [Fact]
    public async Task ASenateCandidateWhoseBillHasNoSavedDetailGetsNoQueueRowBecauseTheDraftNeedsTheBillTitle()
    {
        var job = await EnqueueVoteAsync("vote", 1);

        await store.AdmitSenateVoteAsync(Source(), job, Vote(1, "s", 5), await ExtractionAsync(job), default);

        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM vote_candidates"));
        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM vote_analysis"));
    }

    [Fact]
    public async Task ASecondFetchOfAFileWithTheSameModifyDateRecordsNothingNew()
    {
        var first = await EnqueueVoteAsync("first", 1);
        var second = await EnqueueVoteAsync("second", 1, DaysAgo(1));
        await store.AdmitSenateVoteAsync(Source(), first, Vote(1, "s", 5), await ExtractionAsync(first), default);

        var replay = await store.AdmitSenateVoteAsync(Source(), second, Vote(1, "s", 5), await ExtractionAsync(second), default);
        var sameJobAgain = await store.AdmitSenateVoteAsync(Source(), first, Vote(1, "s", 5), await ExtractionAsync(first), default);

        Assert.Equal((1, 1), (replay.DuplicateCount, sameJobAgain.DuplicateCount));
        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM vote_versions"));
        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM vote_candidates"));
    }

    [Fact]
    public async Task AFileWithANewModifyDateIsANewVersionAndCandidateOfTheSameRoll()
    {
        var first = await EnqueueVoteAsync("first", 1);
        var second = await EnqueueVoteAsync("second", 1, DaysAgo(1));
        await store.AdmitSenateVoteAsync(Source(), first, Vote(1, "s", 5), await ExtractionAsync(first), default);

        await store.AdmitSenateVoteAsync(Source(), second, Vote(1, "s", 5, Modified.AddDays(1)), await ExtractionAsync(second), default);

        Assert.Equal(2L, await ScalarAsync("SELECT count(*) FROM vote_versions"));
        Assert.Equal(2L, await ScalarAsync("SELECT count(DISTINCT candidate_id) FROM vote_candidates"));
    }

    [Fact]
    public async Task AVoteWithTheExtractionOfAnotherJobIsInvalidDataAndRecordsNothing()
    {
        var job = await EnqueueVoteAsync("vote", 1);
        var other = await EnqueueVoteAsync("other", 2);
        var foreign = await ExtractionAsync(other);

        await Assert.ThrowsAsync<InvalidDataException>(() => store.AdmitSenateVoteAsync(Source(), job, Vote(1, null, null), foreign, default));

        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM vote_versions"));
        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM vote_candidates"));
    }

    private static SenateVote Vote(int roll, string? billType, int? billNumber, DateTimeOffset? modified = null) =>
        new(new SenateVoteRoll(SenateVoteFixtures.Session, roll), modified ?? Modified, billType, billNumber);

    private static DateOnly DaysAgo(int days) => DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-days);

    private static SenateVoteMenuItem Item(int roll, DateOnly day) => new(new SenateVoteRoll(SenateVoteFixtures.Session, roll), day);

    private static SenateVoteMenu Menu(params SenateVoteMenuItem[] votes) => new(SenateVoteFixtures.Session, votes);

    private static ConfiguredCollectionSource Source() =>
        new(SenateVoteFixtures.Configuration(), SenateVoteFixtures.SourceId, Backfill.AddDays(400));

    private async Task<DiscoveryAdmissionResult> AdmitMenuAsync(string menuJob, SenateVoteMenu menu)
    {
        var job = await SucceededJobs.RootAsync(store, connectionString, Source(), TestJobKeyPrefix + menuJob);
        return await store.AdmitSenateMenuAsync(Source(), job, menu, default);
    }

    private Task<string> EnqueueVoteAsync(string name, int roll, DateOnly? voteDay = null) =>
        SucceededJobs.SenateVoteAsync(store, connectionString, Source(), TestJobKeyPrefix + name, Item(roll, voteDay ?? DaysAgo(200)));

    private Task<string> ExtractionAsync(string jobId)
    {
        var text = "Senate vote: 119 1 1\n" + Guid.NewGuid().ToString("N");
        return extractions.SaveAsync(jobId, text, text, "extraction");
    }

    private async Task<(string DetailJobId, string Chamber, long Congress, long Session, long Roll, long Ticks, string? BillType, long? BillNumber,
        string ExtractionId)> VersionAsync()
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            SELECT detail_job_id, chamber, congress, session_number, roll_call_number, update_date_utc_ticks, bill_type, bill_number, extraction_id
            FROM vote_versions
            """, connection);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (reader.GetString(0), reader.GetString(1), reader.GetInt32(2), reader.GetInt32(3), reader.GetInt32(4), reader.GetInt64(5),
            reader.IsDBNull(6) ? null : reader.GetString(6), reader.IsDBNull(7) ? null : reader.GetInt32(7), reader.GetString(8));
    }

    private async Task<(string DetailJobId, string MembersJobId, string DetailExtractionId, string MembersExtractionId, string RevisionId)>
        CandidateJobsAsync()
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            SELECT detail_job_id, members_job_id, detail_extraction_id, members_extraction_id, configuration_revision_id FROM vote_candidates
            """, connection);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4));
    }

    private async Task<object?> ScalarAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return await command.ExecuteScalarAsync();
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
