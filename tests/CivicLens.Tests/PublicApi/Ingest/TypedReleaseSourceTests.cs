using CivicLens.PublicApi.Ingest;
using CivicLens.PublicApi.Records;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Infrastructure.Collection;
using CivicLens.Tests.PublicApi.Votes;
using Npgsql;

namespace CivicLens.Tests.PublicApi.Ingest;

/// <summary>The typed reader gives the index documents of every kind from the <c>public_api</c> views. Each test checks them against fixed expected rows.</summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class TypedReleaseSourceTests(PostgresCollection postgres)
{
    private const int Limit = IndexedRecord.SourceLimit;
    private const string VoteSourceHead = "Started: 2025-04-10T11:08:00-04:00\nResult: Passed\nMember: A000055, Jane Doe, R, AL, Yea\n";
    private const string ExecutiveSourceHead = "Federal Register document: 2025-01900\nTitle: Ending the Weaponization\nSigned: 2025-01-20\nPublished: 2025-01-28\n" +
        "Citation: 90 FR 8235\nDocument URL: https://www.federalregister.gov/documents/2025/01/28/2025-01900/x\n";

    private sealed record Texts(string StatementSummary, string StatementSource, string ChangeSummary, string After, string Before, string ActionSummary, string BillTitle,
        string ActionSource, string VoteSummary, string VoteSource, string ExecutiveTail, string BriefParagraph, string Reason)
    {
        public static Texts Short { get; } = new("A short account.", "Xq the source text.", "The rule changed.", "Newer words.", "Older words.", "Doe sponsored it.",
            "A housing bill", "Sponsor: D000001 and more.", "The House passed it.", "Roll call raw text.", "Tail text.", "A brief paragraph.", "The statement describes the bill.");
    }

    /// <summary>The texts that the index stores for one record.</summary>
    private sealed record Row(string Kind, string Summary, string Source, string? Snippet);

    [Fact]
    public async Task EachKindGivesItsFixedSummarySourceAndSnippetTextAndTheEntryOfItsRevision()
    {
        var records = Records(Texts.Short);

        var typed = await ReadAsync(records);

        Row[] expected =
        [
            new("action", "Doe sponsored it.\nA housing bill", "Sponsor: D000001 and more.", "This bill funds housing."),
            new("brief", "A brief paragraph.", "", null),
            new("documentChange", "The rule changed.", "Newer words.\nOlder words.", "The rule changed."),
            new("executiveAction", "", ExecutiveSourceHead + "Tail text.", null),
            new("relationship", "The statement describes the bill.\nthe source", "", null),
            new("statement", "A short account.\nXq", "Xq the source text.", "A short account."),
            new("vote", "The House passed it.", VoteSourceHead + "Roll call raw text.", "The House passed it.")
        ];
        Assert.Equal(expected, typed.Select(ToRow));
        Assert.Equal(records.Select(record => record.Entry).OrderBy(EntryKey, StringComparer.Ordinal).Select(ProjectEntry),
            typed.Select(record => ProjectEntry(record.Entry)));
        Assert.Equal("This bill funds housing.", typed.Single(record => Kind(record) == "action").Entry.BillLine);
        Assert.Equal(["housing", "budget"], typed.Single(record => Kind(record) == "statement").Entry.IssueIds);
    }

    [Fact]
    public async Task EachKindCutsItsLongTextsAtTheFixedLimitsWithoutSplittingACharacter()
    {
        var longSummary = new string('w', 8_000);
        var longSource = "Xq " + new string('s', Limit + 10);
        var splitSource = "Xq " + new string('s', Limit - 4) + "\U0001F600\U0001F600 tail";
        var texts = Texts.Short with
        {
            StatementSummary = longSummary,
            StatementSource = splitSource,
            ChangeSummary = longSummary,
            After = longSource,
            Before = "Old",
            ActionSummary = longSummary,
            BillTitle = "A title",
            ActionSource = splitSource,
            VoteSummary = new string('w', 4_000),
            VoteSource = longSource,
            ExecutiveTail = longSource
        };

        var typed = await ReadAsync(Records(texts));

        // The limit falls inside the first emoji of the statement and action sources, so the cut drops its high surrogate.
        Row[] expected =
        [
            new("action", longSummary + "\nA title", splitSource[..(Limit - 1)], "This bill funds housing."),
            new("brief", "A brief paragraph.", "", null),
            new("documentChange", longSummary, longSource[..Limit], longSummary),
            new("executiveAction", "", (ExecutiveSourceHead + longSource)[..Limit], null),
            // The relationship quote cuts ten characters from the statement source.
            new("relationship", "The statement describes the bill.\nssssssssss", "", null),
            new("statement", longSummary + "\nXq", splitSource[..(Limit - 1)], longSummary),
            new("vote", new string('w', 4_000), (VoteSourceHead + longSource)[..Limit], new string('w', 4_000))
        ];
        Assert.Equal(expected, typed.Select(ToRow));
        Assert.False(char.IsHighSurrogate(typed.Single(record => Kind(record) == "action").Source[^1]));
    }

    [Fact]
    public async Task ADocumentChangeIndexesTheNewVersionBeforeTheOldVersionAsTheRecordFileDoes()
    {
        var typed = await ReadAsync(Records(Texts.Short));

        Assert.Equal("Newer words.\nOlder words.", typed.Single(record => Kind(record) == "documentChange").Source);
    }

    [Fact]
    public async Task AStatementWhoseSourceRowIsMissingIsReportedWithItsRelationshipAndTheOtherRecordsStillGiveTheirDocuments()
    {
        var records = Records(Texts.Short);
        var database = await postgres.CreateMigratedDatabaseAsync();
        try
        {
            await new TypedReleaseSite(database).PublishAsync(1, records);
            await ViewsSeed.ExecuteAsync(database, "SET session_replication_role = replica; DELETE FROM statement_candidates");
            await using var views = await ViewsReaderAsync(database);
            await using var read = (await new TypedReleaseSource(new RecordSnapshot(views)).ReadActiveAsync(CancellationToken.None))!;

            var batch = await read.ReadRecordsAsync(read.Release.Records.ToArray(), CancellationToken.None);

            // The relationship quote reads the source text of the statement, so it fails with the statement.
            Assert.Equal([new string('1', 64), new string('9', 64)], batch.Failures.Select(failure => failure.RecordId).Order(StringComparer.Ordinal));
            Assert.Equal(records.Length - 2, batch.Records.Count);
        }
        finally { await postgres.DropDatabaseAsync(database); }
    }

    [Fact]
    public async Task AReleaseThatPublicationActivatesDuringTheReadDoesNotMixIntoTheReleaseThatTheReadListed()
    {
        var records = Records(Texts.Short);
        var database = await postgres.CreateMigratedDatabaseAsync();
        try
        {
            await new TypedReleaseSite(database).PublishAsync(1, records);
            var expected = await ReadTypedAsync(database);
            await using var views = await ViewsReaderAsync(database);
            await using var read = (await new TypedReleaseSource(new RecordSnapshot(views)).ReadActiveAsync(CancellationToken.None))!;

            // Publication activates release 2 after the list is read. The statement moves to a revision that the read did not list.
            await ViewsSeed.ActivateReleaseAsync(database, 2, DateTimeOffset.UnixEpoch.AddDays(2), records.Length);
            await ViewsSeed.ExecuteAsync(database, "UPDATE publication_record_states SET revision_number = revision_number + 1 WHERE kind = 'statement'");
            var batch = await read.ReadRecordsAsync(read.Release.Records.ToArray(), CancellationToken.None);

            Assert.Equal(1, read.Release.ReleaseNumber);
            Assert.Empty(batch.Failures);
            Assert.Equal(expected.Select(Project), batch.Records.OrderBy(Key, StringComparer.Ordinal).Select(Project));
        }
        finally { await postgres.DropDatabaseAsync(database); }
    }

    private static async Task<IndexedRecord[]> ReadTypedAsync(string database)
    {
        await using var views = await ViewsReaderAsync(database);
        await using var read = (await new TypedReleaseSource(new RecordSnapshot(views)).ReadActiveAsync(CancellationToken.None))!;
        var batch = await read.ReadRecordsAsync(read.Release.Records.ToArray(), CancellationToken.None);
        Assert.Empty(batch.Failures);
        return [.. batch.Records.OrderBy(Key, StringComparer.Ordinal)];
    }

    private static async Task<NpgsqlDataSource> ViewsReaderAsync(string database) => NpgsqlDataSource.Create(await ApiHost.LogInPublicRoleAsync(database));

    private async Task<IndexedRecord[]> ReadAsync(SampleRecord[] records)
    {
        var database = await postgres.CreateMigratedDatabaseAsync();
        try
        {
            await new TypedReleaseSite(database).PublishAsync(1, records);
            var typed = await ReadTypedAsync(database);
            Assert.Equal(records.Length, typed.Length);
            return typed;
        }
        finally { await postgres.DropDatabaseAsync(database); }
    }

    private static SampleRecord[] Records(Texts texts)
    {
        var action = PublishedSamples.Action("Doe sponsors a bill", texts.ActionSummary, texts.BillTitle, texts.ActionSource);
        var executive = PublishedSamples.ExecutiveAction("Ending the Weaponization", texts.ExecutiveTail);
        var executiveId = new string('d', 64);
        executive = executive with
        {
            Entry = executive.Entry with { RecordId = executiveId },
            Body = ((PublishedExecutiveAction)executive.Body) with { RecordId = executiveId }
        };
        var quote = texts.StatementSource.Substring(TypedSearchSeed.QuoteStart, 10);
        return
        [
            PublishedSamples.Statement('1', "Rent cap", texts.StatementSummary, texts.StatementSource, date: new DateOnly(2026, 3, 4), issues: ["housing", "budget"],
                areas: ["economics"], host: "news.example"),
            PublishedSamples.DocumentChange("Rule changed", texts.ChangeSummary, texts.After, texts.Before),
            action with { Entry = action.Entry with { BillLine = "This bill funds housing." } },
            PublishedSamples.Vote("Passage", texts.VoteSummary, texts.VoteSource),
            executive,
            PublishedSamples.Brief("Weekly brief", texts.BriefParagraph),
            PublishedSamples.Relationship("A statement about a bill", texts.Reason, quote)
        ];
    }

    private static string Kind(IndexedRecord record) => PublicationProtocol.KindText(record.Entry.Kind);

    private static string Key(IndexedRecord record) => EntryKey(record.Entry);

    private static string EntryKey(PublishedRecordEntry entry) => PublicationProtocol.KindText(entry.Kind) + entry.RecordId;

    private static Row ToRow(IndexedRecord record) => new(Kind(record), record.Summary, record.Source, record.SnippetText);

    // Every field that the index stores for the record.
    private static string Project(IndexedRecord record) => ProjectEntry(record.Entry) + '\u001f' + string.Join('\u001f', record.Summary, record.Source, record.SnippetText);

    // Every entry field that the index stores for the record.
    private static string ProjectEntry(PublishedRecordEntry entry) => string.Join('\u001f', PublicationProtocol.KindText(entry.Kind), entry.RecordId, entry.RevisionNumber,
        entry.Headline, entry.FirstPublishedAtUtc.UtcTicks, string.Join(',', entry.Officials.Select(official => official.Id + "=" + official.Name)),
        string.Join(',', entry.IssueIds), string.Join(',', entry.PolicyAreaIds), entry.SourceHost, entry.EventDate, entry.BillLine);
}
