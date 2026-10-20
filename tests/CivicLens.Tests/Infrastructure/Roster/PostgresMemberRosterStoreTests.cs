using CivicLens.Application.Collection;
using CivicLens.Application.Collection.Jobs;
using CivicLens.Application.Roster;
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
        var attemptId = await ImportAsync('a', 0);

        await roster.AdmitAsync(attemptId, RosterFixtures.Members(), default);

        var latest = await roster.ReadLatestAsync(RosterFixtures.SourceId, default);
        Assert.Equal(["C000127", "N000147", "W000832"], latest.Select(member => member.BioguideId));
        var cantwell = latest[0];
        Assert.Equal(("S275", "511 Hart Senate Office Building"), (cantwell.LisId, cantwell.Office));
        Assert.Equal([new DateOnly(1993, 1, 5), new DateOnly(2025, 1, 3)], cantwell.Terms.Select(term => term.Start));
        Assert.Equal((3L, 5L), (await CountAsync("roster_members", attemptId), await CountAsync("roster_terms", attemptId)));
    }

    [Fact]
    public async Task ACaptureWithTheBodyOfAnEarlierSnapshotNotOnlyTheLatestIsKnown()
    {
        var a = await ImportAsync('a', 0);
        var b = await ImportAsync('b', 1);
        await roster.AdmitAsync(a, RosterFixtures.Members(), default);
        await roster.AdmitAsync(b, [.. RosterFixtures.Members().Take(2)], default);
        var aAgain = await ImportAsync('a', 2);
        var newBody = await ImportAsync('c', 3);

        Assert.True(await roster.HasSnapshotOfCaptureAsync(aAgain, default));
        Assert.False(await roster.HasSnapshotOfCaptureAsync(newBody, default));
    }

    [Fact]
    public async Task AReplayOfAnAdmittedAttemptMustSkipOrItHitsAPrimaryKeyConflictAndEndsFailed()
    {
        var attemptId = await ImportAsync('a', 0);
        await roster.AdmitAsync(attemptId, RosterFixtures.Members(), default);

        Assert.True(await roster.HasSnapshotOfCaptureAsync(attemptId, default));
    }

    [Fact]
    public async Task ReaderFollowsTheNewestCaptureWhateverTheAdmissionOrder()
    {
        var a = await ImportAsync('a', 0);
        var b = await ImportAsync('b', 1);
        var aAgain = await ImportAsync('a', 2);
        await roster.AdmitAsync(a, RosterFixtures.Members(), default);
        Assert.Equal(3, (await roster.ReadLatestAsync(RosterFixtures.SourceId, default)).Count);
        Assert.True(await roster.HasSnapshotOfCaptureAsync(aAgain, default));

        await roster.AdmitAsync(b, [.. RosterFixtures.Members().Take(2)], default);

        Assert.Equal(3, (await roster.ReadLatestAsync(RosterFixtures.SourceId, default)).Count);
    }

    [Fact]
    public async Task AFileThatChangesAndThenChangesBackReadsAsTheEarlierSnapshotWithoutANewOne()
    {
        var a = await ImportAsync('a', 0);
        var b = await ImportAsync('b', 1);
        await roster.AdmitAsync(a, RosterFixtures.Members(), default);
        await roster.AdmitAsync(b, [.. RosterFixtures.Members().Take(2)], default);
        Assert.Equal(2, (await roster.ReadLatestAsync(RosterFixtures.SourceId, default)).Count);
        var aAgain = await ImportAsync('a', 2);

        Assert.True(await roster.HasSnapshotOfCaptureAsync(aAgain, default));
        Assert.Equal(3, (await roster.ReadLatestAsync(RosterFixtures.SourceId, default)).Count);
    }

    [Fact]
    public async Task TwoSnapshotsOfOneBodyReadAsTheOldestOne()
    {
        var first = await ImportAsync('a', 0);
        var raced = await ImportAsync('a', 1);
        await roster.AdmitAsync(raced, [.. RosterFixtures.Members().Take(1)], default);
        await roster.AdmitAsync(first, RosterFixtures.Members(), default);

        Assert.Equal(3, (await roster.ReadLatestAsync(RosterFixtures.SourceId, default)).Count);
    }

    [Fact]
    public async Task AChangedCaptureAppendsASnapshotAndReadersTakeTheLatestWhileTheOldRowsStay()
    {
        var first = await ImportAsync('a', 0);
        var changed = await ImportAsync('b', 1);
        await roster.AdmitAsync(first, RosterFixtures.Members(), default);

        await roster.AdmitAsync(changed, [.. RosterFixtures.Members().Take(2)], default);

        Assert.Equal(2, (await roster.ReadLatestAsync(RosterFixtures.SourceId, default)).Count);
        Assert.Equal(3, await CountAsync("roster_members", first));
    }

    [Fact]
    public async Task AFailedInsertLeavesNoPartialSnapshot()
    {
        var attemptId = await ImportAsync('a', 0);
        var members = RosterFixtures.Members();
        var broken = members[1] with { Terms = [members[1].Terms[0] with { Chamber = "gov" }] };

        await Assert.ThrowsAsync<DbUpdateException>(() => roster.AdmitAsync(attemptId, [members[0], broken], default));

        Assert.Equal((0L, 0L), (await CountAsync("roster_members", attemptId), await CountAsync("roster_terms", attemptId)));
    }

    [Fact]
    public async Task NoSnapshotReadsAsEmpty()
    {
        Assert.Empty(await roster.ReadLatestAsync(RosterFixtures.SourceId, default));
    }

    [Fact]
    public async Task AnUnknownAttemptIsRejected()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => roster.HasSnapshotOfCaptureAsync("missing", default));
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

    private Task<string> ImportAsync(char body, int day) =>
        RosterFixtures.ImportCaptureAsync(attempts, new string(body, 64), Morning.AddDays(day));

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
