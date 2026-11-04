using System.Text.Json;
using CivicLens.Application.Publication;
using CivicLens.Core.Review;
using CivicLens.Host.Publication;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Publication;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Application.Publication;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Infrastructure.Collection;
using CivicLens.Tests.PublicApi;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.Publication;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class VerifyPublishedEntriesTests(PostgresCollection postgres) : IAsyncLifetime
{
    private static readonly ReviewActor Owner = new("auth0|owner", ReviewRole.Owner);
    private static readonly DateTimeOffset PublishedAt = new(2026, 5, 11, 8, 0, 0, TimeSpan.Zero);
    private readonly FakeReleaseDirectory releases = new();
    private readonly Dictionary<int, string> folders = [];
    private string connectionString = null!;
    private PostgresPublicationStore publications = null!;

    public async Task InitializeAsync()
    {
        connectionString = await postgres.CreateMigratedDatabaseAsync();
        publications = new PostgresPublicationStore(new PooledDbContextFactory<CollectionAttemptDbContext>(
            new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options), TestCodeVersion.Current);
    }

    public Task DisposeAsync() => postgres.DropDatabaseAsync(connectionString);

    [Fact]
    public async Task AReleaseThatTheTablesRebuildByteForByteWithTheOrderAndTheNamesOfItsFilePassesAndTheRunWritesNothing()
    {
        var older = await SeedActionAsync('a', daysAgo: 5);
        var newer = await SeedActionAsync('b', daysAgo: 1);
        await AddReleasesAsync(2);
        await AddRangeAsync(older, 1, null);
        await AddRangeAsync(newer, 2, null);
        Keep(1, older);
        Keep(2, older, newer);
        await FillRowsAsync();
        var before = await ReadAsync("SELECT (SELECT count(*) FROM published_entries) || '|' || (SELECT count(*) FROM published_entry_officials)");

        var (exit, json) = await RunCommandAsync();

        Assert.Equal(0, exit);
        Assert.Equal((2, 0, 0, 0), (json.GetProperty("passed").GetInt32(), json.GetProperty("legacy").GetInt32(), json.GetProperty("failed").GetInt32(),
            json.GetProperty("retired").GetInt32()));
        Assert.Equal(before, await ReadAsync("SELECT (SELECT count(*) FROM published_entries) || '|' || (SELECT count(*) FROM published_entry_officials)"));
    }

    [Fact]
    public async Task AnEntryValueThatChangedInTheRowFailsTheReleaseBecauseTheFileNoLongerMatchesTheTablesAndNamesTheRecord()
    {
        var action = await SeedActionAsync('a');
        var other = await SeedActionAsync('b');
        await AddReleasesAsync(1);
        await AddRangeAsync(action, 1, null);
        await AddRangeAsync(other, 1, null);
        Keep(1, action, other);
        await FillRowsAsync();
        await ExecuteAsync($"UPDATE published_entries SET headline = 'Changed later' WHERE record_id = '{action.RecordId}'");

        var (exit, json) = await RunCommandAsync();

        Assert.Equal(1, exit);
        var failed = Assert.Single(json.GetProperty("failedReleases").EnumerateArray());
        Assert.Equal(("bytesDiffer", $"action:{action.RecordId}", 1), (failed.GetProperty("reason").GetString(), failed.GetProperty("record").GetString(),
            failed.GetProperty("releaseNumber").GetInt32()));
        Assert.Equal(1, json.GetProperty("failedByReason").GetProperty("bytesDiffer").GetInt32());
    }

    [Fact]
    public async Task AStateRowWithNoEntryRowFailsTheReleaseBecauseTheFutureForeignKeyNeedsEveryRangeToHaveItsRow()
    {
        var action = await SeedActionAsync('a');
        await AddReleasesAsync(1);
        await AddRangeAsync(action, 1, null);
        Keep(1, action);

        var (exit, json) = await RunCommandAsync();

        Assert.Equal(1, exit);
        var failed = Assert.Single(json.GetProperty("failedReleases").EnumerateArray());
        Assert.Equal(("entryRowMissing", $"action:{action.RecordId}"), (failed.GetProperty("reason").GetString(), failed.GetProperty("record").GetString()));
    }

    [Fact]
    public async Task AnEntryOutsideItsReleaseRangeFailsTheReleaseBecauseTheStateTableWouldHideARecordThatTheFileServes()
    {
        var inRange = await SeedActionAsync('a');
        var outside = await SeedActionAsync('b');
        await AddReleasesAsync(2);
        await AddRangeAsync(inRange, 1, null);
        await AddRangeAsync(outside, 1, 1);
        Keep(1, inRange, outside);
        Keep(2, inRange, outside);
        await FillRowsAsync();

        var (exit, json) = await RunCommandAsync();

        Assert.Equal(1, exit);
        var failed = Assert.Single(json.GetProperty("failedReleases").EnumerateArray());
        Assert.Equal((2, "entryOutsideRange", $"action:{outside.RecordId}"), (failed.GetProperty("releaseNumber").GetInt32(),
            failed.GetProperty("reason").GetString(), failed.GetProperty("record").GetString()));
        Assert.Equal(1, json.GetProperty("passed").GetInt32());
    }

    [Fact]
    public async Task AStateRowThatTheFileDoesNotListFailsTheReleaseBecauseTheTablesWouldServeARecordThatWasNeverPublished()
    {
        var listed = await SeedActionAsync('a');
        var unlisted = await SeedActionAsync('b');
        await AddReleasesAsync(1);
        await AddRangeAsync(listed, 1, null);
        await AddRangeAsync(unlisted, 1, null);
        Keep(1, listed, unlisted);
        await FillRowsAsync();
        Keep(1, listed);

        var (exit, json) = await RunCommandAsync();

        Assert.Equal(1, exit);
        Assert.Equal("stateRowNotListed", Assert.Single(json.GetProperty("failedReleases").EnumerateArray()).GetProperty("reason").GetString());
    }

    [Fact]
    public async Task AFileThatTheSerializerWouldWriteDifferentlyFailsBeforeAnyTableIsComparedSoASerializerChangeIsNeverBlamedOnTheTables()
    {
        var action = await SeedActionAsync('a');
        await AddReleasesAsync(1);
        await AddRangeAsync(action, 1, null);
        Keep(1, action);
        await FillRowsAsync();
        var path = PublicationProtocol.ManifestPath;
        releases.Directories[folders[1]][path] = [.. releases.Directories[folders[1]][path], (byte)'\n'];

        var (exit, json) = await RunCommandAsync();

        Assert.Equal(1, exit);
        Assert.Equal("manifestNotReproducible", Assert.Single(json.GetProperty("failedReleases").EnumerateArray()).GetProperty("reason").GetString());
    }

    [Fact]
    public async Task ARetiredFolderIsCountedApartAndAnUnreadableManifestFailsBecauseNeitherCanBeProvedFromTheTables()
    {
        var action = await SeedActionAsync('a');
        await AddReleasesAsync(3);
        await AddRangeAsync(action, 1, null);
        Keep(1, action);
        await FillRowsAsync();
        releases.Directories[folders[2]] = new() { [PublicationProtocol.ManifestPath] = "not json"u8.ToArray() };
        Keep(3, action);

        var (exit, json) = await RunCommandAsync();

        Assert.Equal(1, exit);
        Assert.Equal((2, 1, 0), (json.GetProperty("passed").GetInt32(), json.GetProperty("failed").GetInt32(), json.GetProperty("legacy").GetInt32()));
        Assert.Equal("manifestInvalid", json.GetProperty("failedReleases")[0].GetProperty("reason").GetString());
        releases.Directories.Remove(folders[2]);
        var (_, retired) = await RunCommandAsync();
        Assert.Equal(1, retired.GetProperty("retired").GetInt32());
    }

    [Fact]
    public async Task AReleaseWhoseWithdrawalsOnlyTheFileHoldsIsCountedAsLegacyAndDoesNotFailTheRunBecauseEarlyWithdrawalsHaveNoRows()
    {
        var kept = await SeedActionAsync('a');
        var withdrawn = await SeedActionAsync('b');
        await AddReleasesAsync(1);
        await AddRangeAsync(kept, 1, null);
        Keep(1, [kept], [WithdrawalOf(withdrawn)]);
        await FillRowsAsync();

        var (exit, json) = await RunCommandAsync();

        Assert.Equal(0, exit);
        Assert.Equal((0, 1, 0), (json.GetProperty("passed").GetInt32(), json.GetProperty("legacy").GetInt32(), json.GetProperty("failed").GetInt32()));
    }

    [Fact]
    public async Task AWithdrawalThatItsRowsRebuildPassesAndAChangedNoteFailsBecauseTheRowsAreThenTheOwnerOfTheWithdrawals()
    {
        var kept = await SeedActionAsync('a');
        var withdrawn = await SeedActionAsync('b');
        await AddReleasesAsync(1);
        await AddRangeAsync(kept, 1, null);
        var withdrawal = WithdrawalOf(withdrawn);
        Keep(1, [kept], [withdrawal]);
        await FillRowsAsync();
        await ExecuteAsync($"""
            INSERT INTO publication_withdrawals (kind, record_id, withdrawn_revision_number, withdrawn_at_utc_ticks, note, first_release, last_release)
            VALUES ('action', '{withdrawn.RecordId}', 1, {withdrawal.WithdrawnAtUtc.UtcTicks}, '{withdrawal.Note}', 1, NULL)
            """);

        var (passedExit, passed) = await RunCommandAsync();
        await ExecuteAsync($"UPDATE publication_withdrawals SET note = 'Another note.' WHERE record_id = '{withdrawn.RecordId}'");
        var (failedExit, failed) = await RunCommandAsync();

        Assert.Equal((0, 1, 0), (passedExit, passed.GetProperty("passed").GetInt32(), passed.GetProperty("legacy").GetInt32()));
        Assert.Equal(1, failedExit);
        Assert.Equal("bytesDiffer", failed.GetProperty("failedReleases")[0].GetProperty("reason").GetString());
    }

    [Fact]
    public async Task OnlyTheOwnerMayVerifyBecauseTheReportNamesUnpublishedRecordsAndTheCommandRefusesAReviewer()
    {
        var reviewer = new ReviewActor("auth0|reviewer", ReviewRole.Reviewer);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new VerifyPublishedEntries(publications, releases).ExecuteAsync(reviewer));
    }

    private static PublishedWithdrawal WithdrawalOf(PublishedAction action) => new()
    {
        Kind = PublishedRecordKind.Action,
        RecordId = action.RecordId,
        WithdrawnRevisionNumber = action.RevisionNumber,
        FirstPublishedAtUtc = action.FirstPublishedAtUtc,
        WithdrawnAtUtc = action.FirstPublishedAtUtc.AddDays(1),
        Note = "Taken out.",
        Corrections = []
    };

    private void Keep(int number, params PublishedAction[] actions) => Keep(number, actions, []);

    private void Keep(int number, PublishedAction[] actions, PublishedWithdrawal[] withdrawals)
    {
        var files = new Dictionary<string, byte[]>
        {
            [PublicationProtocol.ManifestPath] = JsonSerializer.SerializeToUtf8Bytes(new PublicationRelease
            {
                SchemaVersion = PublicationProtocol.SchemaVersion,
                ReleaseNumber = number,
                PublishedAtUtc = PublishedAt,
                Records = [.. ReleaseRecordOrder.NewestFirst(actions.Select(action => action.ToEntry()), ReleaseRecordOrder.Day, entry => entry.FirstPublishedAtUtc,
                    entry => entry.RecordId)],
                Withdrawals = [.. ReleaseRecordOrder.WithdrawalsNewestFirst(withdrawals)]
            }, PublicationProtocol.JsonOptions)
        };
        foreach (var action in actions)
            files[PublicationProtocol.RecordDataPath(action.RecordId)] = JsonSerializer.SerializeToUtf8Bytes(action, PublicationProtocol.JsonOptions);
        releases.Directories[folders[number]] = files;
    }

    private async Task AddReleasesAsync(int count)
    {
        for (var number = 1; number <= count; number++)
        {
            folders[number] = $"{number:D6}-{number:x32}";
            await ExecuteAsync($"""
                INSERT INTO publication_releases (release_number, directory_name, actor_subject, published_at_utc_ticks, record_count, is_active)
                VALUES ({number}, '{folders[number]}', 'owner', {number}, 0, false)
                """);
        }
    }

    private Task AddRangeAsync(PublishedAction action, int first, int? last) => ExecuteAsync($"""
        INSERT INTO publication_record_states (kind, record_id, revision_number, first_release, last_release)
        VALUES ('action', '{action.RecordId}', {action.RevisionNumber}, {first}, {(last is null ? "NULL" : last.ToString())})
        """);

    private async Task<PublishedAction> SeedActionAsync(char id, int daysAgo = 0)
    {
        var action = (PublishedAction)PublishedSamples.Action("Headline " + id, "A summary.", "A bill title", "Xq the source text.").Body with
        {
            RecordId = new string(id, 64),
            FirstPublishedAtUtc = PublishedAt.AddDays(-daysAgo)
        };
        await ExecuteAsync(PublishedEntryFixtures.InsertActionRevisionSql(action.RecordId, action.RevisionNumber));
        await ExecuteAsync($"INSERT INTO published_records (kind, record_id, first_published_at_utc_ticks) VALUES ('action', '{action.RecordId}', {action.FirstPublishedAtUtc.UtcTicks})");
        return action;
    }

    private Task<PublishedEntriesBackfillResult> FillRowsAsync() => new BackfillPublishedEntries(publications, releases).ExecuteAsync(Owner, 100);

    private async Task<(int Exit, JsonElement Json)> RunCommandAsync()
    {
        var output = new StringWriter();
        var exit = await ReleaseCommand.ExecuteAsync(["releases", "entries", "verify"], null!, null!, null!, null!, null!, null!,
            new VerifyPublishedEntries(publications, releases), Owner, output, TextWriter.Null, CancellationToken.None);
        return (exit, JsonDocument.Parse(output.ToString()).RootElement);
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<List<string>> ReadAsync(string sql) =>
        [.. (await PublishedRecordStateReader.ReadAsync(connectionString, sql)).Select(row => row[0])];
}
