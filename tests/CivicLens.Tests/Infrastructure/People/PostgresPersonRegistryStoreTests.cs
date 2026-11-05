using System.Data.Common;
using CivicLens.Application.Collection;
using CivicLens.Application.People;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.People;
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
    public async Task ImportKeepsEveryPersonIdAndKeyAndTheOfficesWithKindAndDates()
    {
        var result = await people.ImportAsync(Shipped(), Owner, default);

        Assert.Equal((3, 0), (result.Added, result.AlreadyKnown));
        Assert.Equal(
            [("jd-vance", "V000137", "Q28935729"), ("maria-cantwell", "C000127", null), ("president", null, "Q22686")],
            await QueryAsync("SELECT person_id, bioguide_id, wikidata_id FROM people ORDER BY person_id",
                reader => (reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2))));
        Assert.Equal(
            [("jd-vance", 1, 0, "Vice President", "Executive", "2025-01-20", "2029-01-20", null),
             ("president", 1, 0, "President", "Executive", "2025-01-20", null, "Republican"),
             ("president", 1, 1, "Justice", "Judicial", "2001-01-03", "2001-01-04", null)],
            await QueryAsync("""
                SELECT person_id, revision_number, position, title, kind, starts_on::text, ends_on::text, party
                FROM person_office_revision_terms ORDER BY person_id, position
                """,
                reader => (reader.GetString(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetString(3), reader.GetString(4),
                    reader.GetString(5), reader.IsDBNull(6) ? null : reader.GetString(6), reader.IsDBNull(7) ? null : reader.GetString(7))));
        Assert.Equal([(Owner, "Imported from the collection configuration.")],
            await QueryAsync("SELECT DISTINCT actor_subject, note FROM person_office_revisions", reader => (reader.GetString(0), reader.GetString(1))));
    }

    [Fact]
    public async Task EachChangeAddsOneRevisionAndTheNewestWinsSoARestoreBringsBackThePhotoAndHistoryKeepsEveryRow()
    {
        await people.ImportAsync(Shipped(), Owner, default);

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
        await people.ImportAsync(Shipped(), Owner, default);

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
        await people.ImportAsync(Shipped(), Owner, default);
        await people.AppendSettingAsync("president", setting => setting with { WithholdPhoto = true }, "Takedown.", Owner, default);

        var newest = await people.ReadNewestSettingsAsync(["president", "jd-vance", "stranger"], default);

        Assert.Equal(["president"], newest.Keys);
    }

    [Fact]
    public async Task APersonThatDoesNotExistIsRefusedAndWritesNoRevision()
    {
        await people.ImportAsync(Shipped(), Owner, default);

        var failure = await Assert.ThrowsAsync<ArgumentException>(() =>
            people.AppendSettingAsync("stranger", setting => setting with { WithholdPhoto = true }, "Takedown.", Owner, default));

        Assert.Contains("stranger", failure.Message, StringComparison.Ordinal);
        Assert.Equal([0L], await QueryAsync("SELECT count(*) FROM person_setting_revisions", reader => reader.GetInt64(0)));
    }

    [Fact]
    public async Task TheNewestSettingsOfManyPeopleCostOneQuery()
    {
        await people.ImportAsync(Shipped(), Owner, default);
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
    public async Task ASecondImportAddsNothingSoTheCommandIsSafeToRepeat()
    {
        await people.ImportAsync(Shipped(), Owner, default);
        var before = await CountsAsync();

        var result = await people.ImportAsync(Shipped(), Owner, default);

        Assert.Equal((0, 3), (result.Added, result.AlreadyKnown));
        Assert.Equal(before, await CountsAsync());
    }

    [Fact]
    public async Task ABioguideIdOfAnotherPersonIsRefusedAndTheWholeImportWritesNothing()
    {
        await people.ImportAsync([Person("maria-cantwell", bioguideId: "C000127")], Owner, default);
        var clash = new[] { Person("new-person"), Person("other-cantwell", bioguideId: "C000127") };

        var failure = await Assert.ThrowsAsync<ArgumentException>(() => people.ImportAsync(clash, Owner, default));

        Assert.Contains("C000127", failure.Message, StringComparison.Ordinal);
        Assert.Equal([1L], await QueryAsync("SELECT count(*) FROM people", reader => reader.GetInt64(0)));
    }

    [Fact]
    public async Task AWikidataIdOfAnotherPersonIsRefused()
    {
        await people.ImportAsync([Person("president", wikidataId: "Q22686")], Owner, default);

        var failure = await Assert.ThrowsAsync<ArgumentException>(() =>
            people.ImportAsync([Person("new-person"), Person("copy", wikidataId: "Q22686")], Owner, default));

        Assert.Contains("Q22686", failure.Message, StringComparison.Ordinal);
        Assert.Equal([1L], await QueryAsync("SELECT count(*) FROM people", reader => reader.GetInt64(0)));
    }

    [Fact]
    public async Task AGovtrackIdOfAnotherPersonIsRefusedByTheDatabase()
    {
        await ExecuteAsync("INSERT INTO people (person_id, govtrack_id, created_at_utc_ticks, created_by_actor) VALUES ('a', 7, 1, 'o')");

        var failure = await Assert.ThrowsAsync<PostgresException>(() =>
            ExecuteAsync("INSERT INTO people (person_id, govtrack_id, created_at_utc_ticks, created_by_actor) VALUES ('b', 7, 1, 'o')"));

        Assert.Equal("IX_people_govtrack_id", failure.ConstraintName);
    }

    [Fact]
    public async Task AnOfficeWithoutAKindIsRefusedBecauseNoReaderCouldPublishIt()
    {
        var person = Person("someone") with { Offices = [new OfficeConfiguration { Title = "Mayor", StartsOn = new DateOnly(2020, 1, 1) }] };

        await Assert.ThrowsAsync<ArgumentException>(() => people.ImportAsync([person], Owner, default));
        Assert.Equal([0L], await QueryAsync("SELECT count(*) FROM people", reader => reader.GetInt64(0)));
    }

    [Fact]
    public async Task APersonWithRevisionsCannotBeDeletedSoTheHistoryStays()
    {
        await people.ImportAsync(Shipped(), Owner, default);

        var failure = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync("DELETE FROM people WHERE person_id = 'president'"));

        Assert.Equal("FK_person_office_revisions_people_person_id", failure.ConstraintName);
    }

    [Fact]
    public async Task ARevisionNumberCannotBeWrittenTwiceSoARevisionIsNeverReplaced()
    {
        await people.ImportAsync(Shipped(), Owner, default);

        var failure = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync("""
            INSERT INTO person_office_revisions (person_id, revision_number, note, actor_subject, recorded_at_utc_ticks)
            VALUES ('president', 1, 'again', 'owner', 1)
            """));

        Assert.Equal("PK_person_office_revisions", failure.ConstraintName);
    }

    [Fact]
    public async Task EveryOwnerWriteTakesTheNextNumberOfOneSharedSequence()
    {
        await people.ImportAsync(Shipped(), Owner, default);
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
        await people.ImportAsync([Person("someone")], Owner, default);
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

    private static PersonConfiguration Person(string id, string? bioguideId = null, string? wikidataId = null) =>
        new() { Id = id, Name = id, BioguideId = bioguideId, WikidataId = wikidataId };

    private static PersonConfiguration[] Shipped() =>
    [
        new()
        {
            Id = "president",
            Name = "The President",
            WikidataId = "Q22686",
            Offices =
            [
                new OfficeConfiguration { Title = "President", StartsOn = new DateOnly(2025, 1, 20), Party = "Republican", Kind = ConfiguredOfficeKind.Executive },
                new OfficeConfiguration
                {
                    Title = "Justice", StartsOn = new DateOnly(2001, 1, 3), EndsOn = new DateOnly(2001, 1, 4), Kind = ConfiguredOfficeKind.Judicial
                }
            ]
        },
        new()
        {
            Id = "jd-vance",
            Name = "J.D. Vance",
            BioguideId = "V000137",
            WikidataId = "Q28935729",
            Offices = [new OfficeConfiguration
                { Title = "Vice President", StartsOn = new DateOnly(2025, 1, 20), EndsOn = new DateOnly(2029, 1, 20), Kind = ConfiguredOfficeKind.Executive }]
        },
        new()
        {
            Id = "maria-cantwell",
            Name = "Maria Cantwell",
            BioguideId = "C000127"
        }
    ];

    private async Task<(long, long, long, long)> CountsAsync() =>
        (await QueryAsync("SELECT (SELECT count(*) FROM people), (SELECT count(*) FROM person_setting_revisions), " +
            "(SELECT count(*) FROM person_office_revisions), (SELECT count(*) FROM person_office_revision_terms)",
            reader => (reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetInt64(3)))).Single();

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
