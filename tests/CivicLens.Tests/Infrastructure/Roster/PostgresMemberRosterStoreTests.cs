using CivicLens.Application.Collection;
using CivicLens.Application.Collection.Jobs;
using CivicLens.Application.Roster;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Collection.Jobs;
using CivicLens.Infrastructure.Roster;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Infrastructure.Collection;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.Roster;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PostgresMemberRosterStoreTests(PostgresCollection postgres) : IAsyncLifetime
{
    private static readonly DateTimeOffset Morning = new(2026, 10, 19, 6, 0, 0, TimeSpan.Zero);
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
    public async Task ACaptureWritesOneSnapshotOfMembersAndDatedTermsThatCiteItsAttempt()
    {
        var attemptId = await RosterFixtures.ImportCaptureAsync(attempts, new string('a', 64), Morning);

        await roster.AdmitAsync(attemptId, RosterFixtures.Members(), default);

        var latest = await roster.ReadLatestAsync(RosterFixtures.SourceId, default);
        Assert.Equal(["C000127", "N000147", "W000832"], latest.Select(member => member.BioguideId));
        var cantwell = latest[0];
        Assert.Equal(("S275", "511 Hart Senate Office Building"), (cantwell.LisId, cantwell.Office));
        Assert.Equal([new DateOnly(1993, 1, 5), new DateOnly(2025, 1, 3)], cantwell.Terms.Select(term => term.Start));
        Assert.Equal((3L, 5L), (await CountAsync("roster_members", attemptId), await CountAsync("roster_terms", attemptId)));
    }

    [Fact]
    public async Task ACaptureWithTheSameBodyHashAsTheLatestSnapshotWritesNothingNew()
    {
        var first = await RosterFixtures.ImportCaptureAsync(attempts, new string('a', 64), Morning);
        var unchanged = await RosterFixtures.ImportCaptureAsync(attempts, new string('a', 64), Morning.AddDays(1));
        await roster.AdmitAsync(first, RosterFixtures.Members(), default);

        await roster.AdmitAsync(unchanged, RosterFixtures.Members(), default);

        Assert.Equal(0, await CountAsync("roster_members", unchanged));
        Assert.Equal(3, await CountAsync("roster_members", first));
    }

    [Fact]
    public async Task AChangedCaptureAppendsASnapshotAndReadersTakeTheLatestWhileTheOldRowsStay()
    {
        var first = await RosterFixtures.ImportCaptureAsync(attempts, new string('a', 64), Morning);
        var changed = await RosterFixtures.ImportCaptureAsync(attempts, new string('b', 64), Morning.AddDays(1));
        await roster.AdmitAsync(first, RosterFixtures.Members(), default);

        await roster.AdmitAsync(changed, [.. RosterFixtures.Members().Take(2)], default);

        Assert.Equal(2, (await roster.ReadLatestAsync(RosterFixtures.SourceId, default)).Count);
        Assert.Equal(3, await CountAsync("roster_members", first));
    }

    [Fact]
    public async Task AdmittingTheSameAttemptAgainIsHarmlessSoAReplayedJobKeepsOneSnapshot()
    {
        var attemptId = await RosterFixtures.ImportCaptureAsync(attempts, new string('a', 64), Morning);

        await roster.AdmitAsync(attemptId, RosterFixtures.Members(), default);
        await roster.AdmitAsync(attemptId, RosterFixtures.Members(), default);

        Assert.Equal(3, await CountAsync("roster_members", attemptId));
    }

    [Fact]
    public async Task NoSnapshotReadsAsEmpty()
    {
        Assert.Empty(await roster.ReadLatestAsync(RosterFixtures.SourceId, default));
    }

    [Fact]
    public async Task AnUnknownAttemptIsRejected()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => roster.AdmitAsync("missing", RosterFixtures.Members(), default));
    }

    [Fact]
    public async Task TheDailyCheckCreatesARosterJobWithNoCredentialEvenThoughTheSourceCoversNoPeople()
    {
        var jobs = PostgresCollectionJobStore.FromConnectionString(connectionString);
        await jobs.SynchronizeAsync(RosterFixtures.Configuration(), default);
        await ExecuteAsync("UPDATE collection_source_schedules SET next_due_utc_ticks = 0");

        Assert.Equal(1, await jobs.AdvanceDueAsync(10, default));

        var job = Assert.Single(await jobs.ListAsync(10, default));
        Assert.Equal((ApiResource.MemberRoster, RosterFixtures.SourceUrl, null), (job.Definition.ApiResource, job.Definition.Url,
            job.Definition.Credential));
    }

    private async Task<long> CountAsync(string table, string attemptId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"SELECT count(*) FROM {table} WHERE attempt_id = @attempt", connection);
        command.Parameters.AddWithValue("attempt", attemptId);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
