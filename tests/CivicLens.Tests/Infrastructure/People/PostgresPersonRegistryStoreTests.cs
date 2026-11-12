using System.Data.Common;
using CivicLens.Application.People;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.People;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.People;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PostgresPersonRegistryStoreTests(PostgresCollection postgres) : IAsyncLifetime
{
    private const string Owner = "owner-subject";
    private string connectionString = null!;
    private PostgresPersonRegistryStore people = null!;

    public async Task InitializeAsync()
    {
        connectionString = await postgres.CreateMigratedDatabaseAsync();
        people = PostgresPersonRegistryStore.FromConnectionString(connectionString);
    }

    public Task DisposeAsync() => postgres.DropDatabaseAsync(connectionString);

    [Fact]
    public async Task EachChangeAddsOneRevisionAndTheNewestWinsSoARestoreBringsBackThePhotoAndHistoryKeepsEveryRow()
    {
        await SeedAsync();

        var withheld = await people.AppendSettingAsync("maria-cantwell", setting => setting with { WithholdPhoto = true }, "Takedown.", Owner, default);
        var website = await people.AppendSettingAsync("maria-cantwell", setting => setting with { Website = "https://cantwell.senate.gov/" }, "Site.", "other-owner", default);
        var restored = await people.AppendSettingAsync("maria-cantwell", setting => setting with { WithholdPhoto = false }, "Back.", Owner, default);

        Assert.Equal([1, 2, 3], new[] { withheld.RevisionNumber, website.RevisionNumber, restored.RevisionNumber });
        Assert.Equal((false, "https://cantwell.senate.gov/"), (restored.WithholdPhoto, restored.Website));
        Assert.Equal(new PersonSetting(false, "https://cantwell.senate.gov/"),
            (await people.ReadNewestSettingsAsync(["maria-cantwell"], default))["maria-cantwell"]);
        Assert.Equal([(1, true, null, Owner, "Takedown."), (2, true, "https://cantwell.senate.gov/", "other-owner", "Site."),
                (3, false, "https://cantwell.senate.gov/", Owner, "Back.")],
            await QueryAsync("SELECT revision_number, withhold_photo, website, actor_subject, note FROM person_setting_revisions ORDER BY revision_number",
                reader => (reader.GetInt32(0), reader.GetBoolean(1), reader.IsDBNull(2) ? null : reader.GetString(2), reader.GetString(3), reader.GetString(4))));
        Assert.All(await QueryAsync("SELECT recorded_at_utc_ticks FROM person_setting_revisions", reader => reader.GetInt64(0)), ticks => Assert.True(ticks > 0));
    }

    [Fact]
    public async Task TwoChangesOfOnePersonAtTheSameTimeLetOneWinAndWriteNothingForTheOther()
    {
        await SeedAsync();

        var loser = await Assert.ThrowsAsync<DbUpdateException>(() => people.AppendSettingAsync("maria-cantwell", setting =>
        {
            people.AppendSettingAsync("maria-cantwell", other => other with { Website = "https://cantwell.senate.gov/" }, "Winner.", Owner, default)
                .GetAwaiter().GetResult();
            return setting with { WithholdPhoto = true };
        }, "Loser.", Owner, default));

        Assert.Equal("PK_person_setting_revisions", ((PostgresException)loser.InnerException!).ConstraintName);
        Assert.Equal([(1, "Winner.")],
            await QueryAsync("SELECT revision_number, note FROM person_setting_revisions", reader => (reader.GetInt32(0), reader.GetString(1))));
    }

    [Fact]
    public async Task APersonWithNoRevisionIsAbsentFromTheNewestSettingsSoTheReaderUsesNoSetting()
    {
        await SeedAsync();
        await people.AppendSettingAsync("president", setting => setting with { WithholdPhoto = true }, "Takedown.", Owner, default);

        var newest = await people.ReadNewestSettingsAsync(["president", "jd-vance", "stranger"], default);

        Assert.Equal(["president"], newest.Keys);
    }

    [Fact]
    public async Task APersonThatDoesNotExistIsRefusedAndWritesNoRevision()
    {
        await SeedAsync();

        var failure = await Assert.ThrowsAsync<ArgumentException>(() =>
            people.AppendSettingAsync("stranger", setting => setting with { WithholdPhoto = true }, "Takedown.", Owner, default));

        Assert.Contains("stranger", failure.Message, StringComparison.Ordinal);
        Assert.Equal([0L], await QueryAsync("SELECT count(*) FROM person_setting_revisions", reader => reader.GetInt64(0)));
    }

    [Fact]
    public async Task TheNewestSettingsOfManyPeopleCostOneQuery()
    {
        await SeedAsync();
        foreach (var id in new[] { "president", "jd-vance", "maria-cantwell" })
            await people.AppendSettingAsync(id, setting => setting with { WithholdPhoto = true }, "Takedown.", Owner, default);
        await people.AppendSettingAsync("president", setting => setting with { WithholdPhoto = false }, "Back.", Owner, default);
        var counter = new ReadCounter();
        var counted = new PostgresPersonRegistryStore(new PooledDbContextFactory<CollectionAttemptDbContext>(
            new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).AddInterceptors(counter).Options));

        var newest = await counted.ReadNewestSettingsAsync(["president", "jd-vance", "maria-cantwell"], default);

        Assert.Equal(1, counter.Reads);
        Assert.Equal([false, true, true], [newest["president"].WithholdPhoto, newest["jd-vance"].WithholdPhoto, newest["maria-cantwell"].WithholdPhoto]);
    }

    [Fact]
    public async Task TheKeysOfEveryPersonCostOneQueryAndAPersonWithNoBioguideIdKeepsItsId()
    {
        await PersonSeed.InsertAsync(connectionString, "john-smith", "S000001");
        await PersonSeed.InsertAsync(connectionString, "john-smith-s000002", "S000002");
        await ExecuteAsync("INSERT INTO people (person_id, created_at_utc_ticks, created_by_actor) VALUES ('president', 1, 'o')");
        var counter = new ReadCounter();
        var counted = new PostgresPersonRegistryStore(new PooledDbContextFactory<CollectionAttemptDbContext>(
            new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).AddInterceptors(counter).Options));

        var keys = await counted.ReadKeysAsync(default);

        Assert.Equal(1, counter.Reads);
        Assert.Equal([new("john-smith", "S000001"), new("john-smith-s000002", "S000002"), new("president", null)], keys);
    }

    [Theory]
    [InlineData("bioguide_id", "'C000127'", "IX_people_bioguide_id")]
    [InlineData("wikidata_id", "'Q22686'", "IX_people_wikidata_id")]
    [InlineData("govtrack_id", "7", "IX_people_govtrack_id")]
    public async Task AKeyOfAnotherPersonIsRefusedByTheDatabaseSoNoTwoPeopleShareAKey(string column, string value, string index)
    {
        await ExecuteAsync($"INSERT INTO people (person_id, {column}, created_at_utc_ticks, created_by_actor) VALUES ('a', {value}, 1, 'o')");

        var failure = await Assert.ThrowsAsync<PostgresException>(() =>
            ExecuteAsync($"INSERT INTO people (person_id, {column}, created_at_utc_ticks, created_by_actor) VALUES ('b', {value}, 1, 'o')"));

        Assert.Equal(index, failure.ConstraintName);
    }

    [Fact]
    public async Task APersonWithRevisionsCannotBeDeletedSoTheHistoryStays()
    {
        await SeedAsync();

        var failure = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync("DELETE FROM people WHERE person_id = 'president'"));

        Assert.Equal("FK_person_office_revisions_people_person_id", failure.ConstraintName);
    }

    [Fact]
    public async Task ARevisionNumberCannotBeWrittenTwiceSoARevisionIsNeverReplaced()
    {
        await SeedAsync();

        var failure = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync("""
            INSERT INTO person_office_revisions (person_id, revision_number, note, actor_subject, recorded_at_utc_ticks)
            VALUES ('president', 1, 'again', 'owner', 1)
            """));

        Assert.Equal("PK_person_office_revisions", failure.ConstraintName);
    }

    [Fact]
    public async Task EveryOwnerWriteTakesTheNextNumberOfOneSharedSequence()
    {
        await SeedAsync();
        await ExecuteAsync("""
            INSERT INTO person_setting_revisions (person_id, revision_number, withhold_photo, note, actor_subject, recorded_at_utc_ticks)
            VALUES ('president', 1, true, 'takedown', 'owner', 1)
            """);

        var numbers = await QueryAsync("""
            SELECT registry_sequence FROM person_setting_revisions
            UNION ALL SELECT registry_sequence FROM person_office_revisions ORDER BY 1
            """, reader => reader.GetInt64(0));

        Assert.Equal(numbers.Count, numbers.Distinct().Count());
        Assert.Equal(3, numbers.Count);
    }

    [Theory]
    [InlineData("INSERT INTO people (person_id, created_at_utc_ticks, created_by_actor) VALUES ('Bad Id', 1, 'o')", "ck_people_identity")]
    [InlineData("INSERT INTO people (person_id, bioguide_id, created_at_utc_ticks, created_by_actor) VALUES ('a', 'x', 1, 'o')", "ck_people_identity")]
    [InlineData("INSERT INTO people (person_id, created_at_utc_ticks) VALUES ('a', 1)", "ck_people_creator")]
    [InlineData("INSERT INTO people (person_id, created_at_utc_ticks, created_by_actor, created_by_attempt_id) VALUES ('a', 1, 'o', 'x')", "ck_people_creator")]
    public async Task APersonRowOfTheWrongShapeIsRefusedByTheDatabaseToo(string sql, string constraint)
    {
        var failure = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(sql));

        Assert.Equal(constraint, failure.ConstraintName);
    }

    [Theory]
    [InlineData("'Mayor', 'Other', '2020-01-01', NULL", "ck_person_office_revision_terms_kind")]
    [InlineData("'Mayor', 'Judicial', '2020-01-02', '2020-01-01'", "ck_person_office_revision_terms_shape")]
    public async Task ATermOfTheWrongShapeIsRefusedByTheDatabaseToo(string values, string constraint)
    {
        await PersonSeed.InsertAsync(connectionString, "someone");
        await ExecuteAsync("""
            INSERT INTO person_office_revisions (person_id, revision_number, note, actor_subject, recorded_at_utc_ticks)
            VALUES ('someone', 1, 'n', 'o', 1)
            """);

        var failure = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            $"INSERT INTO person_office_revision_terms (person_id, revision_number, position, title, kind, starts_on, ends_on) VALUES ('someone', 1, 0, {values})"));

        Assert.Equal(constraint, failure.ConstraintName);
    }

    private sealed class ReadCounter : DbCommandInterceptor
    {
        public int Reads { get; private set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Reads++;
            return ValueTask.FromResult(result);
        }
    }

    private async Task SeedAsync()
    {
        await PersonSeed.InsertAsync(connectionString, "president", wikidataId: "Q22686");
        await PersonSeed.InsertAsync(connectionString, "jd-vance", "V000137", "Q28935729");
        await PersonSeed.InsertAsync(connectionString, "maria-cantwell", "C000127");
        await PersonSeed.InsertOfficeAsync(connectionString, "president", "President");
        await PersonSeed.InsertOfficeAsync(connectionString, "jd-vance", "Vice President");
    }

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
