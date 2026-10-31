using System.Text.Json;
using CivicLens.Infrastructure.Collection;
using CivicLens.Tests.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.Publication;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PublicationRecordStatesMigrationTests(PostgresCollection postgres) : IAsyncLifetime
{
    private const string Migration = "20261030182714_PublicationRecordStates";
    private const string DropMigration = "20261031175813_DropPublicationRecordsJson";
    private string connectionString = null!;

    public async Task InitializeAsync()
    {
        connectionString = await postgres.CreateEmptyDatabaseAsync();
        await using var db = NewContext();
        await db.GetService<IMigrator>().MigrateAsync(db.Database.GetMigrations().TakeWhile(id => id != Migration).Last());
    }

    public Task DisposeAsync() => postgres.DropDatabaseAsync(connectionString);

    // Release 3 does not exist. A record in releases 2 and 4 still holds one range, because only the order of existing releases counts.
    [Fact]
    public async Task TheBackfillRebuildsEveryReleaseFromRecordsJsonWithLegacyEntriesGapsAndReturnedRecords()
    {
        var legacyDraft = new string('1', 32);
        await InsertReleaseAsync(1, $$"""[{"draftId":"{{legacyDraft}}","revisionNumber":1},{"kind":"action","recordId":"x","revisionNumber":1},{"kind":"statement","recordId":"w","revisionNumber":1}]""");
        await InsertReleaseAsync(2, """[{"kind":"documentChange","recordId":"y","revisionNumber":1},{"kind":"action","recordId":"x","revisionNumber":1},{"kind":"statement","recordId":"w","revisionNumber":2}]""");
        await InsertReleaseAsync(4, """[{"kind":"documentChange","recordId":"y","revisionNumber":1},{"kind":"vote","recordId":"z","revisionNumber":1}]""");
        await InsertReleaseAsync(5, """[{"kind":"documentChange","recordId":"y","revisionNumber":1},{"kind":"action","recordId":"x","revisionNumber":1},{"kind":"vote","recordId":"z","revisionNumber":1}]""", active: true);

        await MigrateForwardAsync();

        await AssertEveryReleaseMatchesRecordsJsonAsync();
        Assert.Equal(["1|2", "5|NULL"], await PublishedRecordStateReader.RangesAsync(connectionString, "x"));
        Assert.Equal(["2|NULL"], await PublishedRecordStateReader.RangesAsync(connectionString, "y"));
        Assert.Equal(["1|1", "2|2"], await PublishedRecordStateReader.RangesAsync(connectionString, "w"));
        Assert.Equal(["4|NULL"], await PublishedRecordStateReader.RangesAsync(connectionString, "z"));
        Assert.Equal(["documentChange"], await KindsAsync(legacyDraft));
    }

    [Fact]
    public async Task AnEntryWithNoRevisionNumberFailsTheMigrationAndLeavesTheSchemaUnchangedBecauseTheMigrationRunsInOneTransaction()
    {
        await InsertReleaseAsync(1, """[{"kind":"action","recordId":"x"}]""", active: true);

        var failure = await Assert.ThrowsAsync<PostgresException>(() => MigrateForwardAsync());

        Assert.Equal(PostgresErrorCodes.NotNullViolation, failure.SqlState);
        Assert.Empty(await PublishedRecordStateReader.ReadAsync(connectionString,
            "SELECT 1 FROM information_schema.tables WHERE table_name = 'publication_record_states'"));
        await using var db = NewContext();
        Assert.Contains(Migration, await db.Database.GetPendingMigrationsAsync());
    }

    [Fact]
    public async Task TheDropRemovesRecordsJsonAndKeepsEveryStateRowBecauseTheTableIsTheOnlySourceAfterIt()
    {
        await InsertReleaseAsync(1, """[{"kind":"action","recordId":"x","revisionNumber":1}]""");
        await InsertReleaseAsync(2, """[{"kind":"action","recordId":"x","revisionNumber":2}]""", active: true);
        await MigrateForwardAsync();
        var before = await StateRowsAsync();

        await MigrateForwardAsync(DropMigration);

        Assert.False(await RecordsJsonExistsAsync());
        Assert.Equal(before, await StateRowsAsync());
        Assert.Equal(2, before.Count);
    }

    [Fact]
    public async Task TheDropFailsAndKeepsRecordsJsonWhenAStateRowDiffersFromTheReleasesBecauseTheDataCannotComeBack()
    {
        await InsertReleaseAsync(1, """[{"kind":"action","recordId":"x","revisionNumber":1}]""");
        await MigrateForwardAsync();
        await InsertReleaseAsync(2, """[{"kind":"action","recordId":"x","revisionNumber":1},{"kind":"action","recordId":"y","revisionNumber":1}]""", active: true);

        var failure = await Assert.ThrowsAsync<PostgresException>(() => MigrateForwardAsync(DropMigration));

        Assert.Equal(PostgresErrorCodes.RaiseException, failure.SqlState);
        Assert.True(await RecordsJsonExistsAsync());
    }

    private async Task<bool> RecordsJsonExistsAsync() =>
        (await PublishedRecordStateReader.ReadAsync(connectionString,
            "SELECT 1 FROM information_schema.columns WHERE table_name = 'publication_releases' AND column_name = 'records_json'")).Count > 0;

    private CollectionAttemptDbContext NewContext() =>
        new(new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options);

    private async Task MigrateForwardAsync(string target = Migration)
    {
        await using var db = NewContext();
        await db.GetService<IMigrator>().MigrateAsync(target);
    }

    private async Task AssertEveryReleaseMatchesRecordsJsonAsync()
    {
        foreach (var release in (await PublishedRecordStateReader.ReadAsync(connectionString, "SELECT release_number, records_json FROM publication_releases")))
        {
            using var document = JsonDocument.Parse(release[1]);
            SortedSet<string> listed = [.. document.RootElement.EnumerateArray().Select(entry => string.Join('|',
                entry.TryGetProperty("kind", out var kind) ? kind.GetString() : "documentChange",
                entry.TryGetProperty("recordId", out var recordId) ? recordId.GetString() : entry.GetProperty("draftId").GetString(),
                entry.GetProperty("revisionNumber").GetInt32()))];
            Assert.Equal(listed, await PublishedRecordStateReader.StateAtAsync(connectionString, int.Parse(release[0])));
        }
    }

    private async Task InsertReleaseAsync(int number, string recordsJson, bool active = false)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            INSERT INTO publication_releases (release_number, directory_name, actor_subject, published_at_utc_ticks, record_count, records_json, is_active)
            VALUES (@number, @directory, 'auth0|owner', 0, 1, @records, @active)
            """, connection);
        command.Parameters.AddWithValue("number", number);
        command.Parameters.AddWithValue("directory", $"{number:D6}-{Guid.NewGuid():N}");
        command.Parameters.AddWithValue("records", recordsJson);
        command.Parameters.AddWithValue("active", active);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<List<string>> StateRowsAsync() =>
        [.. (await PublishedRecordStateReader.ReadAsync(connectionString, """
            SELECT kind || '|' || record_id || '|' || revision_number || '|' || first_release || '|' || COALESCE(last_release::text, 'NULL')
            FROM publication_record_states ORDER BY 1
            """)).Select(row => row[0])];

    private async Task<List<string>> KindsAsync(string recordId) =>
        [.. (await PublishedRecordStateReader.ReadAsync(connectionString,
            $"SELECT kind FROM publication_record_states WHERE record_id = '{recordId}'")).Select(row => row[0])];
}
