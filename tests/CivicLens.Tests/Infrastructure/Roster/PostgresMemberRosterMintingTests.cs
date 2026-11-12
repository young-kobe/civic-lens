using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Roster;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.Roster;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PostgresMemberRosterMintingTests(PostgresCollection postgres) : IAsyncLifetime
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
    public async Task ANewMemberGetsAPersonIdInTheSameTransactionAsTheSnapshotAndCitesItsAttempt()
    {
        var attemptId = await ImportAsync('a', 0);

        await roster.AdmitAsync(attemptId, RosterFixtures.Members(), null, default);

        Assert.Equal(
            [("aisha-wahab", "W000832", attemptId, null), ("eleanor-holmes-norton", "N000147", attemptId, null), ("maria-cantwell", "C000127", attemptId, null)],
            await QueryAsync("SELECT person_id, bioguide_id, created_by_attempt_id, created_by_actor FROM people ORDER BY person_id",
                reader => (reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3))));
        Assert.Equal([true], await QueryAsync("""
            SELECT bool_and(p.xmin::text = s.xmin::text) FROM people p JOIN roster_snapshots s ON s.attempt_id = p.created_by_attempt_id
            """, reader => reader.GetBoolean(0)));
    }

    [Fact]
    public async Task AFailedPeopleInsertLeavesNoSnapshotSoNoMemberIsHalfAdmitted()
    {
        await ExecuteAsync("""
            CREATE FUNCTION refuse_people() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'people refused'; END $$;
            CREATE TRIGGER refuse_people BEFORE INSERT ON people FOR EACH ROW EXECUTE FUNCTION refuse_people();
            """);
        var attemptId = await ImportAsync('a', 0);

        var failure = await Assert.ThrowsAsync<DbUpdateException>(() => roster.AdmitAsync(attemptId, RosterFixtures.Members(), null, default));

        Assert.Contains("people refused", failure.InnerException!.Message, StringComparison.Ordinal);
        Assert.Equal([(0L, 0L, 0L, 0L)], await QueryAsync(
            "SELECT (SELECT count(*) FROM roster_snapshots), (SELECT count(*) FROM roster_members), (SELECT count(*) FROM roster_terms), (SELECT count(*) FROM people)",
            reader => (reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetInt64(3))));
    }

    [Fact]
    public async Task AnIdTakenByAnotherPersonGivesTheNewMemberTheBioguideIdAsASuffix()
    {
        await PersonSeed.InsertAsync(connectionString, "aisha-wahab", "A000001");

        await roster.AdmitAsync(await ImportAsync('a', 0), RosterFixtures.Members(), null, default);

        Assert.Equal([("aisha-wahab", "A000001"), ("aisha-wahab-w000832", "W000832")],
            await QueryAsync("SELECT person_id, bioguide_id FROM people WHERE person_id LIKE 'aisha%' ORDER BY person_id",
                reader => (reader.GetString(0), reader.GetString(1))));
    }

    [Fact]
    public async Task AMemberWhoHasAPersonRowIsNotMintedAgainEvenWhenTheNameWouldGiveAnotherId()
    {
        await PersonSeed.InsertAsync(connectionString, "cantwell", "C000127");

        await roster.AdmitAsync(await ImportAsync('a', 0), RosterFixtures.Members(), null, default);

        Assert.Equal(["aisha-wahab", "cantwell", "eleanor-holmes-norton"], await QueryAsync("SELECT person_id FROM people ORDER BY person_id", reader => reader.GetString(0)));
        Assert.Equal([("seed", null as string)], await QueryAsync("SELECT created_by_actor, created_by_attempt_id FROM people WHERE person_id = 'cantwell'",
            reader => (reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1))));
    }

    [Fact]
    public async Task ASecondSnapshotWithTheSameMembersAddsNoPersonAndARenamedMemberKeepsTheId()
    {
        await roster.AdmitAsync(await ImportAsync('a', 0), RosterFixtures.Members(), null, default);
        var before = await ReadPeopleAsync();
        var renamed = RosterFixtures.Members().Select(member => member.BioguideId == "C000127" ? member with { FullName = "Maria E. Cantwell" } : member).ToArray();

        await roster.AdmitAsync(await ImportAsync('b', 1), renamed, null, default);

        Assert.Equal(2L, (await QueryAsync("SELECT count(*) FROM roster_snapshots", reader => reader.GetInt64(0))).Single());
        Assert.Equal(before, await ReadPeopleAsync());
    }

    private Task<string> ImportAsync(char body, int day) =>
        RosterFixtures.ImportCaptureAsync(attempts, new string(body, 64), Morning.AddDays(day));

    private Task<List<(string, string, string, long)>> ReadPeopleAsync() =>
        QueryAsync("SELECT person_id, bioguide_id, created_by_attempt_id, created_at_utc_ticks FROM people ORDER BY person_id",
            reader => (reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetInt64(3)));

    private async Task<List<T>> QueryAsync<T>(string sql, Func<NpgsqlDataReader, T> read)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<T>();
        while (await reader.ReadAsync()) rows.Add(read(reader));
        return rows;
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
