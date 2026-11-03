using CivicLens.Application.Districts;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Collection.Jobs;
using CivicLens.Infrastructure.Districts;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.Districts;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PostgresZctaDistrictStoreTests(PostgresCollection postgres) : IAsyncLifetime
{
    private static readonly DateTimeOffset Morning = new(2026, 11, 3, 6, 0, 0, TimeSpan.Zero);
    private string connectionString = null!;
    private PostgresCollectionAttemptStore attempts = null!;
    private PostgresZctaDistrictStore districts = null!;

    public async Task InitializeAsync()
    {
        connectionString = await postgres.CreateMigratedDatabaseAsync();
        attempts = PostgresCollectionAttemptStore.FromConnectionString(connectionString);
        districts = PostgresZctaDistrictStore.FromConnectionString(connectionString);
    }

    public Task DisposeAsync() => postgres.DropDatabaseAsync(connectionString);

    [Fact]
    public async Task ACaptureWritesOneHeaderAndEveryRowCitingItsAttempt()
    {
        var attemptId = await ImportAsync('a', 0);

        await districts.AdmitAsync(attemptId, DistrictFixtures.Rows(), default);

        Assert.Equal((1L, 5L), (await CountAsync("roster_snapshots", attemptId), await CountAsync("zcta_districts", attemptId)));
        Assert.Equal(4938464L, await ScalarAsync($"SELECT land_area_square_meters FROM zcta_districts WHERE attempt_id = '{attemptId}' AND zcta = '36009' AND district = 1"));
    }

    [Fact]
    public async Task ACaptureWithTheBodyOfAnEarlierSnapshotIsKnownSoPreparationAddsOnlyItsAttemptRow()
    {
        var first = await ImportAsync('a', 0);
        await districts.AdmitAsync(first, DistrictFixtures.Rows(), default);

        var again = await ImportAsync('a', 7);

        Assert.True(await districts.HasSnapshotOfCaptureAsync(again, default));
        Assert.False(await districts.HasSnapshotOfCaptureAsync(await ImportAsync('b', 14), default));
        Assert.Equal(0L, await CountAsync("zcta_districts", again));
    }

    [Fact]
    public async Task AChangedCaptureAppendsASnapshotAndTheOldRowsStay()
    {
        var first = await ImportAsync('a', 0);
        var second = await ImportAsync('b', 7);
        await districts.AdmitAsync(first, DistrictFixtures.Rows(), default);

        await districts.AdmitAsync(second, [.. DistrictFixtures.Rows().Take(2)], default);

        Assert.Equal((5L, 2L), (await CountAsync("zcta_districts", first), await CountAsync("zcta_districts", second)));
    }

    [Fact]
    public async Task ARowWithAZctaAndDistrictAlreadyInTheSnapshotIsRefusedByTheKey()
    {
        var attemptId = await ImportAsync('a', 0);
        await districts.AdmitAsync(attemptId, DistrictFixtures.Rows(), default);

        var failure = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync($"""
            INSERT INTO zcta_districts (attempt_id, zcta, state, district, land_area_square_meters) VALUES ('{attemptId}', '36009', 'AL', 1, 5)
            """));

        Assert.Equal("PK_zcta_districts", failure.ConstraintName);
    }

    [Fact]
    public async Task ARowWithoutAHeaderIsRefusedSoNoSnapshotExistsWithoutIt()
    {
        var attemptId = await ImportAsync('a', 0);

        var failure = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync($"""
            INSERT INTO zcta_districts (attempt_id, zcta, state, district, land_area_square_meters) VALUES ('{attemptId}', '36009', 'AL', 1, 5)
            """));

        Assert.Equal("FK_zcta_districts_roster_snapshots_attempt_id", failure.ConstraintName);
    }

    [Fact]
    public async Task AFailedInsertLeavesNoPartialSnapshot()
    {
        var attemptId = await ImportAsync('a', 0);
        var broken = DistrictFixtures.Rows()[0] with { Zcta = "3600X" };

        var failure = await Assert.ThrowsAsync<DbUpdateException>(() => districts.AdmitAsync(attemptId, [DistrictFixtures.Rows()[1], broken], default));
        Assert.Equal("ck_zcta_districts_zcta", Assert.IsType<PostgresException>(failure.InnerException).ConstraintName);

        Assert.Equal((0L, 0L), (await CountAsync("roster_snapshots", attemptId), await CountAsync("zcta_districts", attemptId)));
    }

    [Fact]
    public async Task AZeroLandRowIsRefusedByTheDatabase()
    {
        var attemptId = await ImportAsync('a', 0);

        var failure = await Assert.ThrowsAsync<DbUpdateException>(() =>
            districts.AdmitAsync(attemptId, [DistrictFixtures.Rows()[0] with { LandAreaSquareMeters = 0 }], default));

        Assert.Equal("ck_zcta_districts_land", Assert.IsType<PostgresException>(failure.InnerException).ConstraintName);
    }

    [Fact]
    public async Task AnUnknownAttemptIsRejected()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => districts.HasSnapshotOfCaptureAsync("missing", default));
    }

    [Fact]
    public async Task TheWeeklyCheckCreatesADistrictJobAndSchedulesTheNextCheckOneWeekLater()
    {
        var jobs = PostgresCollectionJobStore.FromConnectionString(connectionString);
        await jobs.SynchronizeAsync(DistrictFixtures.Configuration(), default);
        await ExecuteAsync("UPDATE collection_source_schedules SET next_due_utc_ticks = 0");

        Assert.Equal(1, await jobs.AdvanceDueAsync(10, default));

        var job = Assert.Single(await jobs.ListAsync(10, default));
        Assert.Equal((CivicLens.Application.Collection.ApiResource.ZctaDistricts, DistrictFixtures.SourceUrl, null),
            (job.Definition.ApiResource, job.Definition.Url, job.Definition.Credential));
        var nextDue = new DateTime(await ScalarAsync("SELECT next_due_utc_ticks FROM collection_source_schedules"), DateTimeKind.Utc);
        Assert.InRange(nextDue - DateTime.UtcNow, TimeSpan.FromDays(7) - TimeSpan.FromMinutes(5), TimeSpan.FromDays(7) + TimeSpan.FromMinutes(5));
    }

    private Task<string> ImportAsync(char body, int day) =>
        RosterFixtures.ImportCaptureAsync(attempts, new string(body, 64), Morning.AddDays(day), DistrictFixtures.SourceId);

    private async Task<long> CountAsync(string table, string attemptId) =>
        await ScalarAsync($"SELECT count(*) FROM {table} WHERE attempt_id = '{attemptId}'");

    private async Task<long> ScalarAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
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
