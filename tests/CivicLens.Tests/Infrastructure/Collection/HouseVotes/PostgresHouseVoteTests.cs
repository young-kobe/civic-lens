using System.Text.Json;
using CivicLens.Application.Collection;
using CivicLens.Application.Configuration;
using CivicLens.Application.Collection.Discovery;
using CivicLens.Application.Collection.HouseVotes;
using CivicLens.Application.Collection.Jobs;
using CivicLens.Collection.Contracts;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Collection.Jobs;
using CivicLens.Infrastructure.Documents;
using CivicLens.Tests.Fixtures;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.Collection.HouseVotes;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PostgresHouseVoteTests(PostgresCollection postgres) : IAsyncLifetime
{
    private const string SourceUrl = HouseVoteFixtures.SourceUrl;
    private const string TestJobKeyPrefix = "test-job/";
    private static readonly DateOnly Backfill = HouseVoteFixtures.Backfill;
    private static readonly DateTimeOffset Updated = HouseVoteFixtures.Updated;
    private string connectionString = null!;
    private readonly string captureRoot = Path.Combine(Path.GetTempPath(), "house-vote-" + Guid.NewGuid().ToString("N"));
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
    public async Task FirstDueCheckRequestsTheFirstPageOfTheSessionOfTheBackfillDate()
    {
        await MakeSourceDueAsync();

        Assert.Equal(1, await store.AdvanceDueAsync(10, default));

        var job = Assert.Single(await store.ListAsync(10, default));
        Assert.Equal(HouseVoteListUrl.FirstPage(SourceUrl, HouseVoteFixtures.FirstSession), job.Definition.Url);
        Assert.Equal(ApiResource.HouseVoteList, job.Definition.ApiResource);
        Assert.Equal(CollectionMode.Api, job.Definition.Mode);
        Assert.Equal(CollectionCredentials.CongressGov, job.Definition.Credential);
    }

    [Fact]
    public async Task ASweepWithAnUnfinishedListPageIsNotStartedAgain()
    {
        await MakeSourceDueAsync();
        await store.AdvanceDueAsync(10, default);
        await ExecuteAsync("UPDATE collection_source_schedules SET next_due_utc_ticks = 0");

        await store.AdvanceDueAsync(10, default);

        Assert.Single(await store.ListAsync(10, default));
    }

    [Fact]
    public async Task PendingDetailJobsDoNotBlockTheNextDailySweepBecauseOnlyListPagesAreRoots()
    {
        await MakeSourceDueAsync();
        await AdmitListAsync("sweep-one", Page(HouseVoteFixtures.FirstSession, Item(100)));
        await ExecuteAsync("UPDATE collection_source_schedules SET next_due_utc_ticks = 0");

        await store.AdvanceDueAsync(10, default);

        var sweeps = (await store.ListAsync(100, default)).Where(job => job.Definition.ApiResource == ApiResource.HouseVoteList);
        Assert.Equal(2, sweeps.Count());
    }

    [Fact]
    public async Task ListPageAdmitsEachVoteOnceAndTheNextPageAndALaterSweepFindsThemKnown()
    {
        var next = SourceUrl + "/119/1?offset=250&limit=250&format=json";
        var first = Page(HouseVoteFixtures.FirstSession, [Item(100), Item(101), Item(100)], next);

        var result = await AdmitListAsync("sweep-one", first);

        Assert.Equal([SourceUrl + "/119/1/100?format=json", SourceUrl + "/119/1/101?format=json", next], result.Jobs.Select(job => job.Url));
        Assert.Equal(0, result.DuplicateCount);
        var replay = await AdmitListAsync("sweep-one", first);
        Assert.Empty(replay.Jobs);
        Assert.Equal(3, replay.DuplicateCount);
        var nextDay = await AdmitListAsync("sweep-two", first with { Next = null });
        Assert.DoesNotContain(nextDay.Jobs, job => job.Url.Contains("/119/1/10", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AVoteWithANewUpdateDateIsAdmittedAgainWhileUnchangedVotesCostNothing()
    {
        await AdmitListAsync("sweep-one", Page(HouseVoteFixtures.FirstSession, Item(100), Item(101)));

        var result = await AdmitListAsync("sweep-two",
            Page(HouseVoteFixtures.FirstSession, Item(100), Item(101, Updated.AddDays(1))));

        Assert.Equal([SourceUrl + "/119/1/101?format=json"], result.Jobs.Select(job => job.Url).Where(url => url.Contains("/119/1/", StringComparison.Ordinal)));
        Assert.Equal(1, result.DuplicateCount);
    }

    [Fact]
    public async Task TheLastPageOfAnEarlierSessionAdmitsTheFirstPageOfTheFollowingSession()
    {
        var result = await AdmitListAsync("sweep-one", Page(HouseVoteFixtures.FirstSession, Item(100)));

        Assert.Equal(SourceUrl + "/119/2?format=json&limit=250", result.Jobs.Last().Url);
    }

    [Fact]
    public async Task TheLastPageOfTheCurrentSessionAdmitsNoFurtherListPage()
    {
        var current = HouseVoteSession.ForYear(Convert.ToInt32(await ScalarAsync("SELECT EXTRACT(YEAR FROM clock_timestamp() AT TIME ZONE 'UTC')")));

        var result = await AdmitListAsync("sweep-one", Page(current, [Item(100, session: current)], null));

        Assert.Equal([$"{SourceUrl}/{current.Congress}/{current.Number}/100?format=json"], result.Jobs.Select(job => job.Url));
        Assert.Equal(0L, await CountAdmittedAsync(ApiResource.HouseVoteList));
    }

    [Fact]
    public async Task AFullPageWithItsNextPageJobFitsTheBudgetButOneMoreJobIsAnInvalidDeferralThatSavesNothing()
    {
        var votes = Enumerable.Range(1, 251).Select(roll => Item(roll)).ToArray();
        var next = SourceUrl + "/119/1?offset=250&limit=250&format=json";

        await Assert.ThrowsAsync<InvalidDataException>(() => AdmitListAsync("too-many", Page(HouseVoteFixtures.FirstSession, votes, next)));

        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM collection_jobs WHERE idempotency_key NOT LIKE 'test-job/%'"));
    }

    [Theory]
    [InlineData("federal-pilot.json")]
    [InlineData("federal-coverage.json")]
    public async Task ShippedSourceBudgetsAdmitAFullPageAndItsNextPage(string file)
    {
        var shipped = JsonSerializer.Deserialize<CollectionConfiguration>(
            File.ReadAllText(Path.Combine(RepositoryRoot.FullName, "config", file)), ConfigurationJson.Options)!;
        var source = shipped.Sources.Single(candidate => candidate.Id == HouseVoteFixtures.SourceId);
        Assert.False(source.Enabled);
        shipped = shipped with { Sources = [source with { Enabled = true }] };
        var votes = Enumerable.Range(1, 250).Select(roll => Item(roll)).ToArray();
        var page = Page(HouseVoteFixtures.FirstSession, votes, SourceUrl + "/119/1?offset=250&limit=250&format=json");
        var definition = new ConfiguredCollectionSource(shipped, HouseVoteFixtures.SourceId, Backfill.AddDays(400))
            .CreateApiJobDefinition(ApiResource.HouseVoteList, HouseVoteListUrl.FirstPage(SourceUrl, page.Session), Backfill);
        var list = await store.EnqueueAsync(definition, TestJobKeyPrefix + "pilot", default);

        var result = await store.AdmitListPageAsync(new ConfiguredCollectionSource(shipped, HouseVoteFixtures.SourceId,
            Backfill.AddDays(400)), list.JobId, page, default);

        Assert.Equal(251, result.AdmittedCount);
        Assert.Equal(0, result.DeferredCount);
    }

    [Fact]
    public async Task DetailRecordsTheVoteVersionAndAdmitsOneMembersJobKeyedByThatVersion()
    {
        var detail = await EnqueueAsync("detail", ApiResource.HouseVoteDetail, SourceUrl + "/119/1/100?format=json");

        var result = await AdmitDetailAsync(detail, HouseVoteFixtures.Vote(100, Updated));

        var members = Assert.Single(result.Jobs);
        Assert.Equal(SourceUrl + "/119/1/100/members?format=json", members.Url);
        Assert.Equal(HouseVoteJobKeys.Members(HouseVoteFixtures.SourceId, HouseVoteFixtures.Vote(100, Updated)),
            (await store.GetAsync(members.JobId, default))!.IdempotencyKey);
        Assert.Equal((detail, 119L, 1L, 100L, Updated.UtcTicks, "hconres", 14L), await HouseVoteRowAsync());
    }

    [Fact]
    public async Task ARollCallWithoutLegislationIsRecordedWithNoBill()
    {
        var detail = await EnqueueAsync("detail", ApiResource.HouseVoteDetail, SourceUrl + "/119/1/100?format=json");

        await AdmitDetailAsync(detail, HouseVoteFixtures.Vote(100, Updated, billType: null, billNumber: null));

        Assert.Equal((detail, 119L, 1L, 100L, Updated.UtcTicks, null, null), await HouseVoteRowAsync());
    }

    [Fact]
    public async Task ASecondDetailJobOfTheSameVoteVersionRecordsNothingAndAdmitsNoMembersJob()
    {
        var first = await EnqueueAsync("first", ApiResource.HouseVoteDetail, SourceUrl + "/119/1/100?format=json");
        var second = await EnqueueAsync("second", ApiResource.HouseVoteDetail, SourceUrl + "/119/1/100?format=json");
        await AdmitDetailAsync(first, HouseVoteFixtures.Vote(100, Updated));

        var replay = await AdmitDetailAsync(second, HouseVoteFixtures.Vote(100, Updated));
        var sameJobAgain = await AdmitDetailAsync(first, HouseVoteFixtures.Vote(100, Updated));

        Assert.Equal((0, 1), (replay.Jobs.Count, replay.DuplicateCount));
        Assert.Equal((0, 1), (sameJobAgain.Jobs.Count, sameJobAgain.DuplicateCount));
        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM house_votes"));
        Assert.Equal(1L, await CountAdmittedAsync(ApiResource.HouseVoteMembers));
    }

    [Fact]
    public async Task AChangedVoteIsRecordedAsANewVersionWithItsOwnMembersJob()
    {
        var first = await EnqueueAsync("first", ApiResource.HouseVoteDetail, SourceUrl + "/119/1/100?format=json");
        var second = await EnqueueAsync("second", ApiResource.HouseVoteDetail, SourceUrl + "/119/1/100?format=json");
        await AdmitDetailAsync(first, HouseVoteFixtures.Vote(100, Updated));

        var result = await AdmitDetailAsync(second, HouseVoteFixtures.Vote(100, Updated.AddDays(2)));

        Assert.Single(result.Jobs);
        Assert.Equal(2L, await ScalarAsync("SELECT count(*) FROM house_votes"));
        Assert.Equal(2L, await CountAdmittedAsync(ApiResource.HouseVoteMembers));
    }

    [Fact]
    public async Task NoCandidateExistsUntilTheMembersOfTheVoteAreCaptured()
    {
        var detail = await EnqueueAsync("detail", ApiResource.HouseVoteDetail, SourceUrl + "/119/1/100?format=json");

        await AdmitDetailAsync(detail, HouseVoteFixtures.Vote(100, Updated));

        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM house_vote_candidates"));
    }

    [Fact]
    public async Task MembersCaptureTheVoteAsOneCandidateThatCarriesTheBillAndBothJobs()
    {
        var (detail, members) = await DetailWithMembersJobAsync(HouseVoteFixtures.Vote(100, Updated));

        var result = await AdmitMembersAsync(members, HouseVoteFixtures.Vote(100, Updated));

        Assert.Empty(result.Jobs);
        var revision = Source().Revision.Id;
        var candidate = await CandidateAsync();
        Assert.Matches("^[0-9a-f]{64}$", candidate.CandidateId);
        Assert.Equal((119L, 1L, 100L, Updated.UtcTicks, "hconres", 14L, detail, members, revision),
            (candidate.Congress, candidate.Session, candidate.Roll, candidate.Ticks, candidate.BillType, candidate.BillNumber,
                candidate.DetailJobId, candidate.MembersJobId, candidate.RevisionId));
    }

    [Fact]
    public async Task ARollCallWithoutLegislationStillBecomesACandidateBecauseNoRelevanceGateExists()
    {
        var vote = HouseVoteFixtures.Vote(100, Updated, billType: null, billNumber: null);
        var (_, members) = await DetailWithMembersJobAsync(vote);

        await AdmitMembersAsync(members, vote);

        var candidate = await CandidateAsync();
        Assert.Equal((null, null), (candidate.BillType, candidate.BillNumber));
    }

    [Fact]
    public async Task ReplayingTheMembersOfAVoteAddsNoSecondCandidate()
    {
        var vote = HouseVoteFixtures.Vote(100, Updated);
        var (_, members) = await DetailWithMembersJobAsync(vote);
        await AdmitMembersAsync(members, vote);

        var replay = await AdmitMembersAsync(members, vote);

        Assert.Equal(1, replay.DuplicateCount);
        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM house_vote_candidates"));
    }

    [Fact]
    public async Task EachChangedVersionOfAVoteBecomesItsOwnCandidate()
    {
        var first = HouseVoteFixtures.Vote(100, Updated);
        var second = HouseVoteFixtures.Vote(100, Updated.AddDays(2));
        var (_, firstMembers) = await DetailWithMembersJobAsync(first);
        var (_, secondMembers) = await DetailWithMembersJobAsync(second);

        await AdmitMembersAsync(firstMembers, first);
        await AdmitMembersAsync(secondMembers, second);

        Assert.Equal(2L, await ScalarAsync("SELECT count(DISTINCT candidate_id) FROM house_vote_candidates"));
    }

    [Fact]
    public async Task MembersOfAVersionWithoutARecordedDetailAreInvalidDataAndWriteNoCandidate()
    {
        var detailVote = HouseVoteFixtures.Vote(100, Updated);
        var (_, members) = await DetailWithMembersJobAsync(detailVote);

        await Assert.ThrowsAsync<InvalidDataException>(() => AdmitMembersAsync(members,
            HouseVoteFixtures.Vote(100, Updated.AddHours(1))));
        await Assert.ThrowsAsync<InvalidDataException>(() => AdmitMembersAsync(members,
            HouseVoteFixtures.Vote(101, Updated)));

        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM house_vote_candidates"));
    }

    [Fact]
    public async Task ReplayingTheLastPageOfASessionFindsTheFollowingSessionsFirstPageJobKnownBecauseBothRunsShareOnePageKey()
    {
        var page = Page(HouseVoteFixtures.FirstSession, Item(100));
        var first = await AdmitListAsync("sweep-one", page);
        Assert.Equal(SourceUrl + "/119/2?format=json&limit=250", first.Jobs.Last().Url);

        var replay = await AdmitListAsync("sweep-one", page);

        Assert.Empty(replay.Jobs);
        Assert.Equal(2, replay.DuplicateCount);
    }

    [Fact]
    public async Task EachCandidateBindsTheExtractionsOfItsOwnDetailJobAndMembersJobSoDraftingReadsTheExactEvidence()
    {
        var vote = HouseVoteFixtures.Vote(100, Updated);
        var (detail, members) = await DetailWithMembersJobAsync(vote);
        await AdmitMembersAsync(members, vote);

        var bound = await ScalarAsync($"""
            SELECT count(*) FROM house_vote_candidates candidate
            JOIN house_votes vote ON vote.detail_job_id = candidate.detail_job_id
            WHERE candidate.detail_extraction_id = vote.extraction_id
              AND EXISTS (SELECT 1 FROM document_extractions extraction JOIN collection_job_attempts attempt ON attempt.attempt_id = extraction.attempt_id
                          WHERE extraction.extraction_id = candidate.detail_extraction_id AND attempt.job_id = '{detail}')
              AND EXISTS (SELECT 1 FROM document_extractions extraction JOIN collection_job_attempts attempt ON attempt.attempt_id = extraction.attempt_id
                          WHERE extraction.extraction_id = candidate.members_extraction_id AND attempt.job_id = '{members}')
            """);
        Assert.Equal(1L, bound);
    }

    [Fact]
    public async Task ADetailWithAnExtractionOfAnotherJobIsInvalidDataAndRecordsNoVote()
    {
        var detail = await EnqueueAsync("detail", ApiResource.HouseVoteDetail, SourceUrl + "/119/1/100?format=json");
        var other = await EnqueueAsync("other", ApiResource.HouseVoteDetail, SourceUrl + "/119/1/101?format=json");

        var foreign = await ExtractionAsync(other);

        await Assert.ThrowsAsync<InvalidDataException>(() => store.AdmitDetailAsync(Source(), detail,
            HouseVoteFixtures.Vote(100, Updated), foreign, default));

        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM house_votes"));
        Assert.Equal(0L, await CountAdmittedAsync(ApiResource.HouseVoteMembers));
    }

    [Fact]
    public async Task MembersWithTheExtractionOfTheDetailJobAreInvalidDataAndWriteNoCandidate()
    {
        var vote = HouseVoteFixtures.Vote(100, Updated);
        var (detail, members) = await DetailWithMembersJobAsync(vote);

        var foreign = await ExtractionAsync(detail);

        await Assert.ThrowsAsync<InvalidDataException>(() => store.AdmitMembersAsync(Source(), members, vote, foreign, default));
        await Assert.ThrowsAsync<InvalidDataException>(() => store.AdmitMembersAsync(Source(), members, vote,
            new string('a', 64), default));

        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM house_vote_candidates"));
    }

    [Fact]
    public async Task TheDatabaseRefusesABillTypeWithoutANumber()
    {
        var detail = await EnqueueAsync("detail", ApiResource.HouseVoteDetail, SourceUrl + "/119/1/100?format=json");

        var extraction = await ExtractionAsync(detail);

        var refused = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync($"""
            INSERT INTO house_votes (detail_job_id, source_id, congress, session_number, roll_call_number, update_date_utc_ticks, bill_type, bill_number, extraction_id)
            VALUES ('{detail}', '{HouseVoteFixtures.SourceId}', 119, 1, 100, 1, 'hr', NULL, '{extraction}')
            """));

        Assert.Equal(PostgresErrorCodes.CheckViolation, refused.SqlState);
    }

    private async Task<(string Detail, string Members)> DetailWithMembersJobAsync(HouseVote vote)
    {
        var url = HouseVoteRollUrl.Detail(SourceUrl, vote.Roll);
        var detail = await EnqueueAsync("detail-" + Guid.NewGuid().ToString("N"), ApiResource.HouseVoteDetail, url);
        var result = await AdmitDetailAsync(detail, vote);
        return (detail, Assert.Single(result.Jobs).JobId);
    }

    private async Task<DiscoveryAdmissionResult> AdmitDetailAsync(string detailJobId, HouseVote vote) =>
        await store.AdmitDetailAsync(Source(), detailJobId, vote, await ExtractionAsync(detailJobId), default);

    private async Task<DiscoveryAdmissionResult> AdmitMembersAsync(string membersJobId, HouseVote vote) =>
        await store.AdmitMembersAsync(Source(), membersJobId, vote, await ExtractionAsync(membersJobId), default);

    private Task<string> ExtractionAsync(string jobId)
    {
        var text = "House vote: 119 1 100\n" + Guid.NewGuid().ToString("N");
        return extractions.SaveAsync(jobId, text, text, "parser", "normalizer");
    }

    private async Task<string> EnqueueAsync(string name, ApiResource resource, string url)
    {
        var definition = Source().CreateApiJobDefinition(resource, url, Backfill);
        var job = await store.EnqueueAsync(definition, TestJobKeyPrefix + name, default);
        await ExecuteAsync($"UPDATE collection_jobs SET state = 'Succeeded' WHERE job_id = '{job.JobId}'");
        return job.JobId;
    }

    private async Task<DiscoveryAdmissionResult> AdmitListAsync(string listJob, HouseVoteListPage page)
    {
        var url = HouseVoteListUrl.FirstPage(SourceUrl, page.Session);
        var definition = Source().CreateApiJobDefinition(ApiResource.HouseVoteList, url, Backfill);
        var job = await store.EnqueueAsync(definition, TestJobKeyPrefix + listJob, default);
        await ExecuteAsync($"UPDATE collection_jobs SET state = 'Succeeded' WHERE job_id = '{job.JobId}'");
        return await store.AdmitListPageAsync(Source(), job.JobId, page, default);
    }

    private async Task MakeSourceDueAsync()
    {
        await store.SynchronizeAsync(HouseVoteFixtures.Configuration(), default);
        await ExecuteAsync("UPDATE collection_source_schedules SET next_due_utc_ticks = 0");
    }

    private async Task<object?> CountAdmittedAsync(ApiResource resource) => await ScalarAsync($"""
        SELECT count(*) FROM collection_jobs
        WHERE idempotency_key NOT LIKE '{TestJobKeyPrefix}%' AND definition_json::jsonb ->> 'apiResource' = '{JsonSerializer.Serialize(resource).Trim('"')}'
        """);

    private static HouseVoteListPage Page(HouseVoteSession session, params HouseVoteListItem[] votes) => Page(session, votes, null);

    private static HouseVoteListPage Page(HouseVoteSession session, HouseVoteListItem[] votes, string? next) => new(session, votes, next);

    private static HouseVoteListItem Item(int roll, DateTimeOffset? updateDate = null, HouseVoteSession? session = null) =>
        new(new HouseVoteRoll(session ?? HouseVoteFixtures.FirstSession, roll), updateDate ?? Updated);

    private static ConfiguredCollectionSource Source() =>
        new(HouseVoteFixtures.Configuration(), HouseVoteFixtures.SourceId, Backfill.AddDays(400));

    private async Task<(string DetailJobId, long Congress, long Session, long Roll, long Ticks, string? BillType, long? BillNumber)>
        HouseVoteRowAsync()
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            SELECT detail_job_id, congress, session_number, roll_call_number, update_date_utc_ticks, bill_type, bill_number
            FROM house_votes
            """, connection);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (reader.GetString(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3), reader.GetInt64(4),
            reader.IsDBNull(5) ? null : reader.GetString(5), reader.IsDBNull(6) ? null : reader.GetInt32(6));
    }

    private async Task<(string CandidateId, long Congress, long Session, long Roll, long Ticks, string? BillType, long? BillNumber,
        string DetailJobId, string MembersJobId, string RevisionId)> CandidateAsync()
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            SELECT candidate_id, congress, session_number, roll_call_number, update_date_utc_ticks, bill_type, bill_number,
                   detail_job_id, members_job_id, configuration_revision_id
            FROM house_vote_candidates
            """, connection);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (reader.GetString(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3), reader.GetInt64(4),
            reader.IsDBNull(5) ? null : reader.GetString(5), reader.IsDBNull(6) ? null : reader.GetInt32(6),
            reader.GetString(7), reader.GetString(8), reader.GetString(9));
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
