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
public sealed class BackfillPublishedEntriesTests(PostgresCollection postgres) : IAsyncLifetime
{
    private static readonly ReviewActor Owner = new("auth0|owner", ReviewRole.Owner);
    private static readonly string StatementId = new('1', 64);
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
    public async Task ABackfillFillsAnEarlierRevisionFromTheOldestKeptFolderAndWritesTheFactsThatACommitWritesIntoTheTables()
    {
        var action = await SeedActionAsync('a');
        await AddReleasesAsync(3);
        await AddRangeAsync(action, 2, null);
        Keep(1);
        Keep(2, Listed(action, action.ToEntry() with { CosponsorIds = null }));
        Keep(3, Listed(action, action.ToEntry(), withFile: false));

        var result = await RunAsync(10);

        Assert.Equal((1, 1, 0, 0, 0), (result.Examined, result.Added, result.AlreadyPresent, result.Skipped, result.OpenRangesWithoutRow));
        var entry = action.ToEntry();
        Assert.Equal([$"action|{action.RecordId}|1|{entry.Headline}|{entry.SourceHost}|{entry.EventDate:yyyy-MM-dd}|{entry.BillTitle}"],
            await ReadAsync("SELECT kind || '|' || record_id || '|' || revision_number || '|' || headline || '|' || source_host || '|' || event_date || '|' || bill_title FROM published_entries"));
        Assert.Equal(["o1|false"], await ReadAsync("SELECT official_id || '|' || is_matched FROM published_entry_officials"));
        Assert.Equal(2, releases.ReadPaths.Count(path => path == PublicationProtocol.ManifestPath));
        Assert.Equal(1, releases.ReadPaths.Count(path => path == PublicationProtocol.RecordDataPath(action.RecordId)));
    }

    [Fact]
    public async Task AManifestEntryThatDiffersFromItsRecordFileOrWhoseFileIsInvalidOrMissingIsSkippedWithItsReasonAndNoRowIsWritten()
    {
        var good = await SeedActionAsync('a');
        var differs = await SeedActionAsync('b');
        var invalid = await SeedActionAsync('c');
        var missing = await SeedActionAsync('d');
        await AddReleasesAsync(1);
        foreach (var action in new[] { good, differs, invalid, missing }) await AddRangeAsync(action, 1, null);
        Keep(1, Listed(good), Listed(differs, differs.ToEntry() with { Headline = "A headline the record file does not hold" }),
            Listed(invalid, file: "not json"u8.ToArray()), Listed(missing, withFile: false));

        var result = await RunAsync(10);

        Assert.Equal((4, 1, 3), (result.Examined, result.Added, result.Skipped));
        Assert.Equal(new Dictionary<string, int> { ["recordFileDiffers"] = 1, ["recordFileInvalid"] = 2 }, result.SkippedByReason);
        Assert.Equal([(differs.RecordId, "recordFileDiffers"), (invalid.RecordId, "recordFileInvalid"), (missing.RecordId, "recordFileInvalid")],
            result.SkippedEntries.Select(skipped => (skipped.RecordId, skipped.Reason)));
        Assert.Equal([good.RecordId], await ReadAsync("SELECT record_id FROM published_entries"));
        Assert.Equal(3, result.OpenRangesWithoutRow);
    }

    [Fact]
    public async Task TwoKeptFoldersThatHoldOneRevisionWithDifferentFactsBlockTheRowBecauseTheBackfillNeverPicksAValue()
    {
        var differs = await SeedActionAsync('a');
        var onlyNewFields = await SeedActionAsync('b');
        await AddReleasesAsync(3);
        foreach (var action in new[] { differs, onlyNewFields }) await AddRangeAsync(action, 1, null);
        Keep(1, Listed(differs), Listed(onlyNewFields));
        Keep(2, Listed(differs), Listed(onlyNewFields));
        Keep(3, Listed(differs, differs.ToEntry() with { Headline = "Another headline" }), Listed(onlyNewFields, onlyNewFields.ToEntry() with { CosponsorIds = ["o9"] }));

        var result = await RunAsync(10);

        Assert.Equal(new Dictionary<string, int> { ["foldersDiffer"] = 1 }, result.SkippedByReason);
        Assert.Equal([onlyNewFields.RecordId], await ReadAsync("SELECT record_id FROM published_entries"));
        Assert.Equal(differs.RecordId, Assert.Single(result.SkippedEntries).RecordId);
    }

    [Fact]
    public async Task ARerunAddsNothingReadsNoManifestAndCountsTheRowsAlreadyPresent()
    {
        var action = await SeedActionAsync('a');
        await AddReleasesAsync(1);
        await AddRangeAsync(action, 1, null);
        Keep(1, Listed(action));
        await RunAsync(10);
        var reads = releases.ReadPaths.Count;

        var rerun = await RunAsync(10);

        Assert.Equal((0, 0, 1, 0, 0), (rerun.Examined, rerun.Added, rerun.AlreadyPresent, rerun.Skipped, rerun.OpenRangesWithoutRow));
        Assert.Equal(reads, releases.ReadPaths.Count);
        Assert.Equal(["1"], await ReadAsync("SELECT count(*) FROM published_entries"));
    }

    [Fact]
    public async Task ALimitBoundsTheInsertedRowsAndTheNextRunContinuesWhereItStopped()
    {
        var actions = await SeedActionsAsync(30);
        await AddReleasesAsync(1);
        foreach (var action in actions) await AddRangeAsync(action, 1, null);
        Keep(1, [.. actions.Select(action => Listed(action))]);

        var limited = await RunAsync(3);
        var rest = await RunAsync(100);

        Assert.Equal((3, 27), (limited.Added, limited.OpenRangesWithoutRow));
        Assert.Equal((27, 3, 0), (rest.Added, rest.AlreadyPresent, rest.OpenRangesWithoutRow));
        Assert.Equal(["30"], await ReadAsync("SELECT count(*) FROM published_entries"));
    }

    [Fact]
    public async Task AFailureInALaterBatchKeepsTheRowsOfTheEarlierBatchOf25()
    {
        var actions = await SeedActionsAsync(26);
        await AddReleasesAsync(1);
        foreach (var action in actions) await AddRangeAsync(action, 1, null);
        Keep(1, [.. actions.Select(action => Listed(action))]);
        var store = new InterceptingPublicationStore(publications)
        {
            BeforeAddingEntries = batch => batch == 2 ? throw new InvalidOperationException("The database went away.") : Task.CompletedTask
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => new BackfillPublishedEntries(store, releases).ExecuteAsync(Owner, 100));

        Assert.Equal(["25"], await ReadAsync("SELECT count(*) FROM published_entries"));
    }

    [Fact]
    public async Task ARevisionThatOnlyARetiredFolderHeldGetsNoRowAndCountsAsAClosedRangeWithoutAKeptFolderWithoutFailingTheCommand()
    {
        var old = await SeedActionAsync('a');
        var returned = old with { RevisionNumber = 2 };
        await SeedActionRevisionAsync(returned);
        await AddReleasesAsync(2);
        await AddRangeAsync(old, 1, 1);
        await AddRangeAsync(returned, 2, null);
        Keep(2, Listed(returned));

        var (exit, json) = await RunCommandAsync(10);

        Assert.Equal(0, exit);
        Assert.Equal((1, 1, 0), (json.GetProperty("added").GetInt32(), json.GetProperty("closedRangesWithoutKeptFolder").GetInt32(), json.GetProperty("skipped").GetInt32()));
        Assert.Equal(["2"], await ReadAsync("SELECT revision_number FROM published_entries"));
    }

    [Fact]
    public async Task AnOpenRangeThatNoKeptFolderHoldsIsReportedAndTheCommandExitsWithOne()
    {
        var action = await SeedActionAsync('a');
        await AddReleasesAsync(1);
        await AddRangeAsync(action, 1, null);

        var (exit, json) = await RunCommandAsync(10);

        Assert.Equal(1, exit);
        Assert.Equal((1, 0, 0), (json.GetProperty("openRangesWithoutRow").GetInt32(), json.GetProperty("added").GetInt32(), json.GetProperty("skipped").GetInt32()));
        Assert.Empty(await ReadAsync("SELECT record_id FROM published_entries"));
    }

    [Fact]
    public async Task ALimitThatStopsBeforeAReadyClosedRangeLeavesItCountedAndTheCommandExitsWithOneUntilARerunWritesIt()
    {
        var open = await SeedActionAsync('a');
        var closed = await SeedActionAsync('b');
        await AddReleasesAsync(2);
        await AddRangeAsync(open, 1, null);
        await AddRangeAsync(closed, 1, 1);
        Keep(1, Listed(open), Listed(closed));
        Keep(2, Listed(open));

        var (limitedExit, limited) = await RunCommandAsync(1);
        var (restExit, rest) = await RunCommandAsync(1);

        Assert.Equal((1, 1, 0, 1), (limited.GetProperty("added").GetInt32(), limited.GetProperty("readyRangesWithoutRow").GetInt32(),
            limited.GetProperty("openRangesWithoutRow").GetInt32(), limitedExit));
        Assert.Equal([open.RecordId], await ReadAsync($"SELECT record_id FROM published_entries WHERE record_id = '{open.RecordId}'"));
        Assert.Equal((1, 0, 0), (rest.GetProperty("added").GetInt32(), rest.GetProperty("readyRangesWithoutRow").GetInt32(), restExit));
        Assert.Equal(["2"], await ReadAsync("SELECT count(*) FROM published_entries"));
    }

    [Fact]
    public async Task AKeptFolderInTheRangeThatOmitsTheRevisionOrListsAnotherOneBlocksTheRowEvenWhenALaterFolderHoldsIt()
    {
        var omitted = await SeedActionAsync('a');
        var otherRevision = await SeedActionAsync('b');
        var good = await SeedActionAsync('c');
        await SeedActionRevisionAsync(otherRevision with { RevisionNumber = 2 });
        await AddReleasesAsync(2);
        foreach (var action in new[] { omitted, otherRevision, good }) await AddRangeAsync(action, 1, null);
        Keep(1, Listed(good), Listed(otherRevision with { RevisionNumber = 2 }));
        Keep(2, Listed(omitted), Listed(otherRevision), Listed(good));

        var result = await RunAsync(10);

        Assert.Equal(new Dictionary<string, int> { ["foldersDiffer"] = 2 }, result.SkippedByReason);
        Assert.Equal([good.RecordId], await ReadAsync("SELECT record_id FROM published_entries"));
    }

    [Fact]
    public async Task AFolderThatListsARevisionAfterTheFirstFolderSetTheEntryButOmitsItLaterStillBlocksTheRowEvenWhenTheNewestFolderHoldsItAgain()
    {
        var action = await SeedActionAsync('a');
        await AddReleasesAsync(3);
        await AddRangeAsync(action, 1, null);
        Keep(1, Listed(action));
        Keep(2);
        Keep(3, Listed(action));

        var result = await RunAsync(10);

        Assert.Equal(new Dictionary<string, int> { ["foldersDiffer"] = 1 }, result.SkippedByReason);
        Assert.Empty(await ReadAsync("SELECT record_id FROM published_entries"));
    }

    [Fact]
    public async Task AReleaseThatACommitAddsBetweenTheTwoReadsNeverMakesTheOlderRevisionSkipAsFoldersDiffer()
    {
        var old = await SeedActionAsync('a');
        var returned = old with { RevisionNumber = 2 };
        await SeedActionRevisionAsync(returned);
        await AddReleasesAsync(1);
        await AddRangeAsync(old, 1, null);
        Keep(1, Listed(old));
        var store = new InterceptingPublicationStore(publications) { BeforeListingAll = () => CommitReleaseTwoAsync(old, returned) };

        var result = await new BackfillPublishedEntries(store, releases).ExecuteAsync(Owner, 10);

        Assert.Empty(result.SkippedByReason);
        Assert.Equal(["1", "2"], await ReadAsync("SELECT revision_number FROM published_entries ORDER BY 1"));
    }

    [Fact]
    public async Task AConflictWithARowThatACommitWroteTellsTheOperatorToCompareTheRowAndNotToRebuildTheRelease()
    {
        var action = await SeedActionAsync('a');
        await AddReleasesAsync(1);
        await AddRangeAsync(action, 1, null);
        Keep(1, Listed(action));
        var store = new InterceptingPublicationStore(publications) { AfterReadingCandidates = () => InsertLikeACommitAsync(action, "The commit headline") };

        var conflict = await Assert.ThrowsAsync<PublicationConflictException>(() => new BackfillPublishedEntries(store, releases).ExecuteAsync(Owner, 10));

        Assert.Contains("Compare the row with the kept folders", conflict.Message);
        Assert.DoesNotContain("Rebuild the release", conflict.Message);
    }

    [Fact]
    public async Task ASkippedRowMakesTheCommandExitWithOneAndTheOtherRowsAreStillWritten()
    {
        var good = await SeedActionAsync('a');
        var bad = await SeedActionAsync('b');
        await AddReleasesAsync(1);
        await AddRangeAsync(good, 1, null);
        await AddRangeAsync(bad, 1, null);
        Keep(1, Listed(good), Listed(bad, file: "not json"u8.ToArray()));

        var (exit, json) = await RunCommandAsync(10);

        Assert.Equal(1, exit);
        Assert.Equal(1, json.GetProperty("skippedByReason").GetProperty("recordFileInvalid").GetInt32());
        Assert.Equal([good.RecordId], await ReadAsync("SELECT record_id FROM published_entries"));
    }

    [Fact]
    public async Task AWithdrawalNeverYieldsAnEntryRowAndAReturnedRevisionIsFilledFromItsFolder()
    {
        var first = await SeedActionAsync('a');
        var returned = first with { RevisionNumber = 2 };
        var withdrawnOnly = await SeedActionAsync('b');
        await SeedActionRevisionAsync(returned);
        await AddReleasesAsync(5);
        await AddRangeAsync(first, 1, 1);
        await AddRangeAsync(returned, 3, null);
        await AddRangeAsync(withdrawnOnly, 4, 4);
        Keep(1, Listed(first));
        KeepWithdrawals(2, WithdrawalOf(first));
        Keep(3, Listed(returned));
        Write(5, [Listed(returned)], [WithdrawalOf(withdrawnOnly)]);

        var result = await RunAsync(10);

        Assert.Equal(["1", "2"], await ReadAsync($"SELECT revision_number FROM published_entries WHERE record_id = '{first.RecordId}' ORDER BY 1"));
        Assert.Empty(await ReadAsync($"SELECT record_id FROM published_entries WHERE record_id = '{withdrawnOnly.RecordId}'"));
        Assert.Equal((2, 0, 1), (result.Added, result.Skipped, result.ClosedRangesWithoutKeptFolder));
    }

    [Fact]
    public async Task ARecordWithNoFirstPublicationTimeIsSkippedWithItsReasonBecauseTheEntryRowNeedsThatTime()
    {
        var action = await SeedActionAsync('a', withFirstPublished: false);
        await AddReleasesAsync(1);
        await AddRangeAsync(action, 1, null);
        Keep(1, Listed(action));

        var result = await RunAsync(10);

        Assert.Equal(new Dictionary<string, int> { ["firstPublishedMissing"] = 1 }, result.SkippedByReason);
        Assert.Empty(await ReadAsync("SELECT record_id FROM published_entries"));
    }

    [Fact]
    public async Task ARelationshipWhoseTargetHasNoFirstPublicationTimeIsSkippedWithItsReasonAndIsWrittenOnceTheTimeExists()
    {
        var relationship = (PublishedRelationship)PublishedSamples.Relationship("A link", "The statement describes the bill.", "quote").Body;
        await ExecuteAsync($$"""
            SET session_replication_role = replica;
            INSERT INTO relationship_revisions (record_id, revision_number, author_subject, author_kind, revision_json, created_at_utc_ticks)
            VALUES ('{{relationship.RecordId}}', 1, 'owner', 'Person', '{}', 0);
            INSERT INTO published_records (kind, record_id, first_published_at_utc_ticks)
            VALUES ('statement', '{{StatementId}}', 1), ('relationship', '{{relationship.RecordId}}', 1)
            """);
        await AddReleasesAsync(1);
        await AddRangeAsync(relationship.RecordId, "relationship", 1, null);
        var entry = relationship.ToEntry();
        Keep(1, new FolderRecord(entry, relationship));

        var skipped = await RunAsync(10);
        await ExecuteAsync($"INSERT INTO published_records (kind, record_id, first_published_at_utc_ticks) VALUES ('action', '{relationship.Target.RecordId}', 1)");
        var written = await RunAsync(10);

        Assert.Equal(new Dictionary<string, int> { ["firstPublishedMissing"] = 1 }, skipped.SkippedByReason);
        Assert.Equal((0, 1), (skipped.Added, written.Added));
        Assert.Equal([relationship.RecordId], await ReadAsync("SELECT record_id FROM published_entries"));
    }

    [Fact]
    public async Task ARowThatACommitInsertsBetweenTheCandidateReadAndTheInsertIsCountedAsPresentAndNotWrittenTwice()
    {
        var action = await SeedActionAsync('a');
        await AddReleasesAsync(1);
        await AddRangeAsync(action, 1, null);
        Keep(1, Listed(action));
        var store = new InterceptingPublicationStore(publications) { AfterReadingCandidates = () => InsertLikeACommitAsync(action, action.Headline) };

        var result = await new BackfillPublishedEntries(store, releases).ExecuteAsync(Owner, 10);

        Assert.Equal((0, 1, 0), (result.Added, result.AlreadyPresent, result.OpenRangesWithoutRow));
        Assert.Equal(["1"], await ReadAsync("SELECT count(*) FROM published_entries"));
        Assert.Equal(["1"], await ReadAsync("SELECT count(*) FROM published_entry_officials"));
    }

    [Fact]
    public async Task ARowThatACommitInsertsWithOtherFactsStopsTheRunAndStaysAsTheCommitWroteIt()
    {
        var action = await SeedActionAsync('a');
        await AddReleasesAsync(1);
        await AddRangeAsync(action, 1, null);
        Keep(1, Listed(action));
        var store = new InterceptingPublicationStore(publications) { AfterReadingCandidates = () => InsertLikeACommitAsync(action, "The commit headline") };

        await Assert.ThrowsAsync<PublicationConflictException>(() => new BackfillPublishedEntries(store, releases).ExecuteAsync(Owner, 10));

        Assert.Equal(["The commit headline"], await ReadAsync("SELECT headline FROM published_entries"));
    }

    private sealed record FolderRecord(PublishedRecordEntry Entry, object? File);

    private FolderRecord Listed(PublishedAction action, PublishedRecordEntry? entry = null, bool withFile = true, byte[]? file = null) =>
        new(entry ?? action.ToEntry(), file is not null ? file : withFile ? action : null);

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

    private void Keep(int number, params FolderRecord[] records) => Write(number, records, []);

    private void KeepWithdrawals(int number, params PublishedWithdrawal[] withdrawals) => Write(number, [], withdrawals);

    private void Write(int number, FolderRecord[] records, PublishedWithdrawal[] withdrawals)
    {
        var files = new Dictionary<string, byte[]>
        {
            [PublicationProtocol.ManifestPath] = JsonSerializer.SerializeToUtf8Bytes(new PublicationRelease
            {
                SchemaVersion = PublicationProtocol.SchemaVersion,
                ReleaseNumber = number,
                PublishedAtUtc = new DateTimeOffset(2026, 5, 11, 8, 0, 0, TimeSpan.Zero),
                Withdrawals = withdrawals,
                Records = [.. records.Select(record => record.Entry)]
            }, PublicationProtocol.JsonOptions)
        };
        foreach (var record in records.Where(record => record.File is not null))
            files[PublicationProtocol.RecordDataPath(record.Entry.RecordId)] =
                record.File as byte[] ?? JsonSerializer.SerializeToUtf8Bytes(record.File, record.File!.GetType(), PublicationProtocol.JsonOptions);
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

    private Task AddRangeAsync(PublishedAction action, int first, int? last) => AddRangeAsync(action.RecordId, "action", action.RevisionNumber, first, last);

    private Task AddRangeAsync(string recordId, string kind, int first, int? last) => AddRangeAsync(recordId, kind, 1, first, last);

    private Task AddRangeAsync(string recordId, string kind, int revision, int first, int? last) => ExecuteAsync($"""
        INSERT INTO publication_record_states (kind, record_id, revision_number, first_release, last_release)
        VALUES ('{kind}', '{recordId}', {revision}, {first}, {(last is null ? "NULL" : last.ToString())})
        """);

    private async Task<PublishedAction> SeedActionAsync(char id, bool withFirstPublished = true)
    {
        var action = (PublishedAction)PublishedSamples.Action("Headline " + id, "A summary.", "A bill title", "Xq the source text.").Body with
        {
            RecordId = new string(id, 64)
        };
        await SeedActionRevisionAsync(action);
        if (withFirstPublished) await AddFirstPublishedAsync("action", action.RecordId);
        return action;
    }

    private Task AddFirstPublishedAsync(string kind, string recordId) =>
        ExecuteAsync($"INSERT INTO published_records (kind, record_id, first_published_at_utc_ticks) VALUES ('{kind}', '{recordId}', 1)");

    private async Task<PublishedAction[]> SeedActionsAsync(int count)
    {
        List<PublishedAction> actions = [];
        for (var index = 0; index < count; index++)
        {
            var action = (PublishedAction)PublishedSamples.Action($"Headline {index}", "A summary.", "A bill title", "Xq the source text.").Body with
            {
                RecordId = index.ToString("x64")
            };
            await SeedActionRevisionAsync(action);
            await AddFirstPublishedAsync("action", action.RecordId);
            actions.Add(action);
        }
        return [.. actions];
    }

    private Task SeedActionRevisionAsync(PublishedAction action) => ExecuteAsync(PublishedEntryFixtures.InsertActionRevisionSql(action.RecordId, action.RevisionNumber));

    private async Task CommitReleaseTwoAsync(PublishedAction old, PublishedAction returned)
    {
        folders[2] = $"{2:D6}-{2:x32}";
        await ExecuteAsync($"""
            INSERT INTO publication_releases (release_number, directory_name, actor_subject, published_at_utc_ticks, record_count, is_active)
            VALUES (2, '{folders[2]}', 'owner', 2, 0, false);
            UPDATE publication_record_states SET last_release = 1 WHERE record_id = '{old.RecordId}' AND revision_number = 1
            """);
        await AddRangeAsync(returned, 2, null);
        Keep(2, Listed(returned));
    }

    private Task InsertLikeACommitAsync(PublishedAction action, string headline)
    {
        var entry = action.ToEntry();
        return ExecuteAsync($"""
            INSERT INTO published_entries (kind, record_id, revision_number, action_record_id, action_revision_number, headline, source_host, event_date, bill_title)
            VALUES ('action', '{action.RecordId}', 1, '{action.RecordId}', 1, '{headline}', '{entry.SourceHost}', '{entry.EventDate:yyyy-MM-dd}', '{entry.BillTitle}');
            INSERT INTO published_entry_officials (kind, record_id, revision_number, position, official_id, is_matched)
            VALUES ('action', '{action.RecordId}', 1, 0, 'o1', false)
            """);
    }

    private Task<PublishedEntriesBackfillResult> RunAsync(int limit) => new BackfillPublishedEntries(publications, releases).ExecuteAsync(Owner, limit);

    private async Task<(int Exit, JsonElement Json)> RunCommandAsync(int limit)
    {
        var output = new StringWriter();
        var exit = await ReleaseCommand.ExecuteAsync(["releases", "entries", "backfill", limit.ToString()], null!, null!, null!, null!, null!,
            new BackfillPublishedEntries(publications, releases), Owner, output, TextWriter.Null, CancellationToken.None);
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
