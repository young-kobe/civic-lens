using CivicLens.Application.Collection;
using CivicLens.Application.Collection.Jobs;
using CivicLens.Application.Collection.MemberPhotos;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Collection.Jobs;
using CivicLens.Infrastructure.Roster;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.Roster;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PostgresMemberPhotoJobTests(PostgresCollection postgres) : IAsyncLifetime
{
    private static readonly DateTimeOffset Morning = new(2026, 10, 28, 6, 0, 0, TimeSpan.Zero);
    private static readonly string[] ConfiguredBioguideIds = ["B000002", "C000127", "N000147"];
    private string connectionString = null!;
    private PostgresCollectionAttemptStore attempts = null!;
    private PostgresMemberRosterStore roster = null!;

    public async Task InitializeAsync()
    {
        connectionString = await postgres.CreateMigratedDatabaseAsync();
        attempts = PostgresCollectionAttemptStore.FromConnectionString(connectionString);
        roster = PostgresMemberRosterStore.FromConnectionString(connectionString);
    }

    public Task DisposeAsync() => postgres.DropDatabaseAsync(connectionString);

    [Fact]
    public async Task ASnapshotAndOneBackfillPhotoJobForEachConfiguredPersonWithABioguideIdAreWrittenTogether()
    {
        var attemptId = await ImportRosterAsync('a', 0);

        await roster.AdmitAsync(attemptId, RosterFixtures.Members(), Photos(), default);

        Assert.Equal(1L, await CountAsync("roster_snapshots"));
        var jobs = await ReadPhotoJobsAsync();
        Assert.Equal(ConfiguredBioguideIds.Select(id => MemberPhotoJobKeys.Photo(MemberPhotoFixtures.SourceId, id, Today())),
            jobs.Select(job => job.IdempotencyKey).Order());
        Assert.All(jobs, job => Assert.Equal(CollectionJobPriority.Backfill, job.Priority));
    }

    [Fact]
    public async Task AJobBindsToItsPhotoUrlAndResourceSoTheWorkerCanLeaseIt()
    {
        await roster.AdmitAsync(await ImportRosterAsync('a', 0), RosterFixtures.Members(), Photos(), default);
        var job = (await ReadPhotoJobsAsync()).First(row => row.IdempotencyKey.Contains("/C000127/"));

        var record = await PostgresCollectionJobStore.FromConnectionString(connectionString).GetAsync(job.JobId, default);

        Assert.Equal((ApiResource.MemberPhoto, MemberPhotoFixtures.PhotoUrl("C000127")),
            (record!.Definition.ApiResource, record.Definition.Url));
        record.Definition.Validate();
    }

    [Fact]
    public async Task ARosterBodyWithoutAPhotoSourceWritesTheSnapshotAndNoJob()
    {
        await roster.AdmitAsync(await ImportRosterAsync('a', 0), RosterFixtures.Members(), null, default);

        Assert.Equal((1L, 0L), (await CountAsync("roster_snapshots"), await CountAsync("collection_jobs")));
    }

    [Fact]
    public async Task ARepeatedRosterJobInTheSamePeriodAddsNoPhotoJobSoTheUniqueKeyMakesTheEnqueueIdempotent()
    {
        await roster.AdmitAsync(await ImportRosterAsync('a', 0), RosterFixtures.Members(), Photos(), default);

        await roster.AdmitPhotoJobsAsync(Photos(), default);
        await roster.AdmitAsync(await ImportRosterAsync('b', 1), RosterFixtures.Members(), Photos(), default);

        Assert.Equal(ConfiguredBioguideIds.Length, (await ReadPhotoJobsAsync()).Count);
    }

    [Fact]
    public async Task ANewPeriodAddsOneJobForEachPersonSoPhotosAreRecheckedEveryThirtyDays()
    {
        await roster.AdmitPhotoJobsAsync(Photos(), default);
        await ExecuteAsync("""
            UPDATE collection_jobs
            SET idempotency_key = regexp_replace(idempotency_key, '/[0-9]+$', '/' || ((regexp_replace(idempotency_key, '^.*/', ''))::int - 1)::text)
            """);

        await roster.AdmitPhotoJobsAsync(Photos(), default);

        Assert.Equal(ConfiguredBioguideIds.Length * 2, (await ReadPhotoJobsAsync()).Count);
    }

    [Fact]
    public async Task AFailedPhotoJobInsertLeavesNoPartialSnapshot()
    {
        var attemptId = await ImportRosterAsync('a', 0);
        await SeedBlockedVoteAsync();
        var tooLongKey = new string('s', 260);
        var configuration = MemberPhotoFixtures.Configuration(People()) with
        {
            Sources = [RosterFixtures.Source(), MemberPhotoFixtures.Source() with { Id = tooLongKey }]
        };

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            roster.AdmitAsync(attemptId, RosterFixtures.Members(), new ConfiguredCollectionSource(configuration, tooLongKey), default));

        Assert.Equal((0L, 0L, 0L), (await CountAsync("roster_snapshots"), await CountAsync("roster_members"), await CountAsync("collection_jobs")));
        Assert.Equal("Blocked", await VoteStatusAsync());
    }

    private ConfiguredCollectionSource Photos() =>
        new(MemberPhotoFixtures.Configuration(People()), MemberPhotoFixtures.SourceId);

    private static PersonConfiguration[] People() =>
    [
        RosterFixtures.Person("cantwell", "Maria Cantwell", "C000127"),
        RosterFixtures.Person("norton", "Eleanor Norton", "N000147"),
        RosterFixtures.Person("other", "Another Person", "B000002"),
        RosterFixtures.Person("no-id", "No Bioguide", null)
    ];

    private static DateOnly Today() => DateOnly.FromDateTime(DateTime.UtcNow);

    private Task<string> ImportRosterAsync(char body, int day) =>
        RosterFixtures.ImportCaptureAsync(attempts, new string(body, 64), Morning.AddDays(day));

    private async Task<List<PhotoJob>> ReadPhotoJobsAsync()
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT job_id, idempotency_key, priority FROM collection_jobs WHERE source_id = @source ORDER BY idempotency_key", connection);
        command.Parameters.AddWithValue("source", MemberPhotoFixtures.SourceId);
        await using var reader = await command.ExecuteReaderAsync();
        var jobs = new List<PhotoJob>();
        while (await reader.ReadAsync()) jobs.Add(new PhotoJob(reader.GetString(0), reader.GetString(1), reader.GetInt16(2)));
        return jobs;
    }

    private async Task<long> CountAsync(string table)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"SELECT count(*) FROM {table}", connection);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private async Task<string> VoteStatusAsync()
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT status FROM vote_analysis", connection);
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private Task SeedBlockedVoteAsync() => ExecuteAsync($"""
        SET session_replication_role = replica;
        INSERT INTO vote_candidates (candidate_id, source_id, chamber, congress, session_number, roll_call_number, update_date_utc_ticks,
            detail_job_id, members_job_id, detail_extraction_id, members_extraction_id, configuration_revision_id, created_at_utc_ticks)
        VALUES ('{new string('1', 64)}', 'votes', 'Senate', 119, 1, 1, 1, 'detail-1', 'members-1',
                '{new string('c', 64)}', '{new string('c', 64)}', '{new string('c', 64)}', 1);
        INSERT INTO vote_analysis (candidate_id, task, status, error_code, fence, attempts, reserved_tokens, created_at_utc_ticks)
        VALUES ('{new string('1', 64)}', 'vote-draft', 'Blocked', 'evidenceUnavailable', 0, 1, 0, 1)
        """);

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private sealed record PhotoJob(string JobId, string IdempotencyKey, int Priority);
}
