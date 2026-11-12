using CivicLens.PublicApi.Ingest;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Infrastructure.Collection;
using CivicLens.Tests.PublicApi.Votes;
using Npgsql;

namespace CivicLens.Tests.PublicApi.Ingest;

/// <summary>The typed reader gives the same index documents as the release JSON feed, which is its oracle, for every kind.</summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class TypedReleaseSourceTests(PostgresCollection postgres)
{
    private const int Limit = IndexedRecord.SourceLimit;
    private static readonly string[] Kinds = ["statement", "documentChange", "action", "vote", "executiveAction", "brief", "relationship"];

    private sealed record Texts(string StatementSummary, string StatementSource, string ChangeSummary, string After, string Before, string ActionSummary, string BillTitle,
        string ActionSource, string VoteSummary, string VoteSource, string ExecutiveTail, string BriefParagraph, string Reason)
    {
        public static Texts Short { get; } = new("A short account.", "Xq the source text.", "The rule changed.", "Newer words.", "Older words.", "Doe sponsored it.",
            "A housing bill", "Sponsor: D000001 and more.", "The House passed it.", "Roll call raw text.", "Tail text.", "A brief paragraph.", "The statement describes the bill.");
    }

    [Fact]
    public async Task TheTypedReaderGivesTheSameIndexDocumentsAsTheReleaseFeedForEveryKindSoTheSwitchChangesNoSearchResult()
    {
        var (typed, feed) = await ReadBothAsync(Texts.Short);

        Assert.Equal(Kinds.Order(StringComparer.Ordinal), typed.Select(Kind).Order(StringComparer.Ordinal));
        Assert.Equal(feed.Select(Project), typed.Select(Project));
        Assert.Contains(typed, record => record.Entry.Kind == PublishedRecordKind.Action && record.SnippetText == "This bill funds housing.");
        Assert.Contains(typed, record => record.Entry.Kind == PublishedRecordKind.Statement && record.Entry.IssueIds.SequenceEqual(["housing", "budget"]));
    }

    [Fact]
    public async Task EachKindCutsItsLongTextsAtTheSameLimitsAsTheReleaseFeedWithoutSplittingACharacter()
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

        var (typed, feed) = await ReadBothAsync(texts);

        Assert.Equal(feed.Select(Project), typed.Select(Project));
        foreach (var kind in new[] { "documentChange", "vote", "executiveAction" })
            Assert.Equal(Limit, typed.Single(record => Kind(record) == kind).Source.Length);
        // The limit falls inside the first emoji of these two sources, so the cut drops its high surrogate.
        foreach (var kind in new[] { "statement", "action" })
            Assert.Equal(Limit - 1, typed.Single(record => Kind(record) == kind).Source.Length);
        Assert.False(char.IsHighSurrogate(typed.Single(record => Kind(record) == "action").Source[^1]));
    }

    [Fact]
    public async Task ADocumentChangeIndexesTheNewVersionBeforeTheOldVersionAsTheRecordFileDoes()
    {
        var (typed, _) = await ReadBothAsync(Texts.Short);

        Assert.Equal("Newer words.\nOlder words.", typed.Single(record => Kind(record) == "documentChange").Source);
    }

    [Fact]
    public async Task AStatementWhoseSourceRowIsMissingIsReportedWithItsRelationshipAndTheOtherRecordsStillGiveTheirDocuments()
    {
        var records = Records(Texts.Short);
        var database = await postgres.CreateMigratedDatabaseAsync();
        try
        {
            await TypedSearchSeed.WriteAsync(database, 1, records);
            await ViewsSeed.ExecuteAsync(database, "SET session_replication_role = replica; DELETE FROM statement_candidates");
            await ViewsSeed.ExecuteAsync(database, "ALTER ROLE civic_lens_public LOGIN PASSWORD 'typed-test'");
            await using var views = NpgsqlDataSource.Create(new NpgsqlConnectionStringBuilder(database) { Username = "civic_lens_public", Password = "typed-test" }.ConnectionString);
            var source = new TypedReleaseSource(views);
            var release = (await source.ReadChangedAsync(CancellationToken.None))!;

            var batch = await source.ReadRecordsAsync(release, release.Records.ToArray(), CancellationToken.None);

            // The relationship quote reads the source text of the statement, so it fails with the statement.
            Assert.Equal([new string('1', 64), new string('9', 64)], batch.Failures.Select(failure => failure.RecordId).Order(StringComparer.Ordinal));
            Assert.Equal(records.Length - 2, batch.Records.Count);
        }
        finally { await postgres.DropDatabaseAsync(database); }
    }

    private async Task<(IndexedRecord[] Typed, IndexedRecord[] Feed)> ReadBothAsync(Texts texts)
    {
        var records = Records(texts);
        var database = await postgres.CreateMigratedDatabaseAsync();
        try
        {
            await TypedSearchSeed.WriteAsync(database, 1, records);
            var site = new FakeReleaseSite();
            site.Publish(1, records);
            var feed = new FeedReleaseSource(new ReleaseFeed(new HttpClient(site) { BaseAddress = new Uri("http://release.test/") }));
            var oracleRelease = (await feed.ReadChangedAsync(CancellationToken.None))!;
            var oracle = await feed.ReadRecordsAsync(oracleRelease, oracleRelease.Records.ToArray(), CancellationToken.None);

            await ViewsSeed.ExecuteAsync(database, "ALTER ROLE civic_lens_public LOGIN PASSWORD 'typed-test'");
            await using var views = NpgsqlDataSource.Create(new NpgsqlConnectionStringBuilder(database) { Username = "civic_lens_public", Password = "typed-test" }.ConnectionString);
            var source = new TypedReleaseSource(views);
            var release = (await source.ReadChangedAsync(CancellationToken.None))!;
            var typed = await source.ReadRecordsAsync(release, release.Records.ToArray(), CancellationToken.None);

            Assert.Empty(oracle.Failures);
            Assert.Empty(typed.Failures);
            Assert.Equal(records.Length, release.Records.Count);
            return ([.. typed.Records.OrderBy(Key, StringComparer.Ordinal)], [.. oracle.Records.OrderBy(Key, StringComparer.Ordinal)]);
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

    private static string Key(IndexedRecord record) => Kind(record) + record.Entry.RecordId;

    // Every field that the index stores for the record.
    private static string Project(IndexedRecord record)
    {
        var entry = record.Entry;
        return string.Join('\u001f', Kind(record), entry.RecordId, entry.RevisionNumber, entry.Headline, entry.FirstPublishedAtUtc.UtcTicks,
            string.Join(',', entry.Officials.Select(official => official.Id + "=" + official.Name)), string.Join(',', entry.IssueIds), string.Join(',', entry.PolicyAreaIds),
            entry.SourceHost, entry.EventDate, entry.BillLine, record.Summary, record.Source, record.SnippetText);
    }
}
