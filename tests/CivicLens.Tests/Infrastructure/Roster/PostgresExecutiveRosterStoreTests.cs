using CivicLens.Application.Roster;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Roster;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.Roster;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PostgresExecutiveRosterStoreTests(PostgresCollection postgres) : IAsyncLifetime
{
    private static readonly DateTimeOffset Morning = new(2026, 10, 19, 6, 0, 0, TimeSpan.Zero);
    private string connectionString = null!;
    private PostgresCollectionAttemptStore attempts = null!;
    private PostgresExecutiveRosterStore executives = null!;
    private PostgresMemberRosterStore members = null!;

    public async Task InitializeAsync()
    {
        connectionString = await postgres.CreateMigratedDatabaseAsync();
        attempts = PostgresCollectionAttemptStore.FromConnectionString(connectionString);
        executives = PostgresExecutiveRosterStore.FromConnectionString(connectionString);
        members = PostgresMemberRosterStore.FromConnectionString(connectionString);
    }

    public Task DisposeAsync() => postgres.DropDatabaseAsync(connectionString);

    [Fact]
    public async Task ACaptureWritesOneHeaderWithEveryOfficialAndDatedTermThatCiteItsAttempt()
    {
        var attemptId = await ImportExecutiveAsync('a', 0);

        await executives.AdmitAsync(attemptId, ExecutiveFixtures.Officials(), default);

        Assert.Equal((1L, 3L, 7L), (await CountAsync("roster_snapshots", attemptId), await CountAsync("executive_officials", attemptId),
            await CountAsync("executive_terms", attemptId)));
        Assert.True(await executives.HasSnapshotOfCaptureAsync(attemptId, default));
    }

    [Fact]
    public async Task ACaptureWithTheBodyOfAnEarlierSnapshotNotOnlyTheLatestIsKnown()
    {
        var a = await ImportExecutiveAsync('a', 0);
        var b = await ImportExecutiveAsync('b', 1);
        await executives.AdmitAsync(a, ExecutiveFixtures.Officials(), default);
        await executives.AdmitAsync(b, [.. ExecutiveFixtures.Officials().Take(2)], default);

        Assert.True(await executives.HasSnapshotOfCaptureAsync(await ImportExecutiveAsync('a', 2), default));
        Assert.False(await executives.HasSnapshotOfCaptureAsync(await ImportExecutiveAsync('c', 3), default));
    }

    [Fact]
    public async Task ACongressSnapshotNeverSatisfiesTheExecutiveHashRuleAndTheExecutiveSnapshotNeverSatisfiesTheCongressRule()
    {
        var congress = await RosterFixtures.ImportCaptureAsync(attempts, new string('a', 64), Morning);
        var executive = await ImportExecutiveAsync('b', 1);
        await members.AdmitAsync(congress, RosterFixtures.Members(), default);
        await executives.AdmitAsync(executive, ExecutiveFixtures.Officials(), default);
        var executiveWithCongressBody = await ImportExecutiveAsync('a', 2);
        var congressWithExecutiveBody = await RosterFixtures.ImportCaptureAsync(attempts, new string('b', 64), Morning.AddDays(3));

        Assert.False(await executives.HasSnapshotOfCaptureAsync(executiveWithCongressBody, default));
        Assert.False(await members.HasSnapshotOfCaptureAsync(congressWithExecutiveBody, default));
        Assert.Equal(3, (await members.ReadLatestAsync(RosterFixtures.SourceId, default)).Count);
        Assert.Empty(await members.ReadLatestAsync(ExecutiveFixtures.SourceId, default));
    }

    [Fact]
    public async Task AFailedInsertLeavesNoPartialSnapshot()
    {
        var attemptId = await ImportExecutiveAsync('a', 0);
        var officials = ExecutiveFixtures.Officials();
        var broken = officials[1] with { Terms = [officials[1].Terms[0] with { Office = "king" }] };

        await Assert.ThrowsAsync<DbUpdateException>(() => executives.AdmitAsync(attemptId, [officials[0], broken], default));

        Assert.Equal((0L, 0L, 0L), (await CountAsync("roster_snapshots", attemptId), await CountAsync("executive_officials", attemptId),
            await CountAsync("executive_terms", attemptId)));
    }

    [Fact]
    public async Task AnOfficialRowWithoutAHeaderIsRefusedSoNoSnapshotExistsWithoutIt()
    {
        var attemptId = await ImportExecutiveAsync('a', 0);

        var failure = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync($"""
            INSERT INTO executive_officials (attempt_id, govtrack_id, full_name) VALUES ('{attemptId}', 1, 'A')
            """));

        Assert.Equal("FK_executive_officials_roster_snapshots_attempt_id", failure.ConstraintName);
    }

    [Theory]
    [InlineData("'q76'")]
    [InlineData("'Q076'")]
    [InlineData("'Q'")]
    public async Task AMalformedWikidataIdIsRefusedByTheDatabase(string wikidata)
    {
        var attemptId = await ImportExecutiveAsync('a', 0);
        await executives.AdmitAsync(attemptId, [new ExecutiveOfficial(1, null, "A", [])], default);

        var failure = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync($"""
            INSERT INTO executive_officials (attempt_id, govtrack_id, wikidata_id, full_name) VALUES ('{attemptId}', 2, {wikidata}, 'B')
            """));

        Assert.Equal("ck_executive_officials_identity", failure.ConstraintName);
    }

    [Fact]
    public async Task AnUnknownAttemptIsRejected()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => executives.HasSnapshotOfCaptureAsync("missing", default));
    }

    [Fact]
    public async Task TheDailyCheckCreatesAnExecutiveJobWithNoCredentialEvenThoughTheSourceCoversNoPeople()
    {
        var jobs = CivicLens.Infrastructure.Collection.Jobs.PostgresCollectionJobStore.FromConnectionString(connectionString);
        await jobs.SynchronizeAsync(ExecutiveFixtures.Configuration(), default);
        await ExecuteAsync("UPDATE collection_source_schedules SET next_due_utc_ticks = 0");

        Assert.Equal(1, await jobs.AdvanceDueAsync(10, default));

        var job = Assert.Single(await jobs.ListAsync(10, default));
        Assert.Equal((CivicLens.Application.Collection.ApiResource.ExecutiveRoster, ExecutiveFixtures.SourceUrl, null),
            (job.Definition.ApiResource, job.Definition.Url, job.Definition.Credential));
    }

    private Task<string> ImportExecutiveAsync(char body, int day) =>
        RosterFixtures.ImportCaptureAsync(attempts, new string(body, 64), Morning.AddDays(day), ExecutiveFixtures.SourceId);

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
