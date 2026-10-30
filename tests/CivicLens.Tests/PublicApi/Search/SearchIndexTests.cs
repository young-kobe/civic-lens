using CivicLens.Application.Publication;
using CivicLens.PublicApi;
using CivicLens.PublicApi.Ingest;
using CivicLens.PublicApi.Search;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Infrastructure.Collection;
using Npgsql;

namespace CivicLens.Tests.PublicApi.Search;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class SearchIndexTests(PostgresCollection postgres) : IAsyncLifetime
{
    private SearchWorld world = null!;

    public async Task InitializeAsync() => world = await SearchWorld.CreateAsync(postgres);

    public async Task DisposeAsync() => await world.DisposeAsync();

    [Fact]
    public async Task AHeadlineMatchRanksAboveAMatchInTheSourceTextBecauseTheHeadlineStatesWhatTheRecordIsAbout()
    {
        await world.PublishAndIngestAsync(1,
            PublishedSamples.Statement('a', "Zoning meeting", source: "Xq the mayor mentioned a rent cap once."),
            PublishedSamples.Statement('b', "Rent cap passes", source: "Xq the council voted."));

        var page = await world.SearchAsync(request => request with { Text = "rent cap" });

        Assert.Equal([new string('b', 64), new string('a', 64)], page.Hits.Select(hit => hit.RecordId));
    }

    [Fact]
    public async Task ASummaryMatchRanksAboveASourceTextMatch()
    {
        await world.PublishAndIngestAsync(1,
            PublishedSamples.Statement('a', "First record", source: "Xq the mayor mentioned vouchers once."),
            PublishedSamples.Statement('b', "Second record", summary: "Vouchers help tenants.", source: "Xq the council voted."));

        var page = await world.SearchAsync(request => request with { Text = "vouchers" });

        Assert.Equal(new string('b', 64), page.Hits[0].RecordId);
    }

    [Fact]
    public async Task TheSnippetShowsTheMatchBetweenMarkersAndLeavesSourceMarkupAsPlainText()
    {
        await world.PublishAndIngestAsync(1,
            PublishedSamples.Statement('a', "Zoning meeting", source: "Xq the <script>alert(1)</script> mayor discussed a rent cap today."));

        var hit = Assert.Single((await world.SearchAsync(request => request with { Text = "rent" })).Hits);

        Assert.Contains("rent", hit.Snippet, StringComparison.Ordinal);
        Assert.DoesNotContain("<b>", hit.Snippet, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EveryKindIsFoundByTheTextOfItsSource()
    {
        await world.PublishAndIngestAsync(1,
            PublishedSamples.Statement('a', "Statement headline", source: "Xq statementword"),
            PublishedSamples.DocumentChange("Change headline", "Change summary", "Afterword text."),
            PublishedSamples.Action("Action headline", "Action summary", "Bill title", "Sponsor: actionword"),
            PublishedSamples.Vote("Vote headline", "Vote summary", "Result: voteword"),
            PublishedSamples.Brief("Brief headline", "Paragraph briefword."),
            PublishedSamples.Relationship("Link headline", "Reason linkword.", "a quote"));

        foreach (var (word, kind) in new[]
        {
            ("statementword", PublishedRecordKind.Statement), ("afterword", PublishedRecordKind.DocumentChange),
            ("actionword", PublishedRecordKind.Action), ("voteword", PublishedRecordKind.Vote),
            ("briefword", PublishedRecordKind.Brief), ("linkword", PublishedRecordKind.Relationship)
        })
            Assert.Equal(kind, Assert.Single((await world.SearchAsync(request => request with { Text = word })).Hits).Kind);
    }

    [Fact]
    public async Task TextBeyondTheSourceLimitIsNotSearchableAndTextBeforeItIs()
    {
        var source = "Xq earlyword " + new string('w', IndexedRecord.SourceLimit) + " lateword";
        await world.PublishAndIngestAsync(1, PublishedSamples.Statement('a', "Rent cap", source: source));

        Assert.Single((await world.SearchAsync(request => request with { Text = "earlyword" })).Hits);
        Assert.Empty((await world.SearchAsync(request => request with { Text = "lateword" })).Hits);
    }

    [Fact]
    public async Task ASourceTextOfManyDistinctWordsStaysUnderThePostgresVectorLimit()
    {
        var words = string.Join(' ', Enumerable.Range(0, 40_000).Select(i => "w" + i.ToString("x", System.Globalization.CultureInfo.InvariantCulture)));
        var result = await world.PublishAndIngestAsync(1, PublishedSamples.Statement('a', "Rent cap", source: "Xq " + words));

        Assert.Equal(1, result.Indexed);
    }

    [Fact]
    public async Task FiltersNarrowTheResultAndEachOneUsesTheManifestEntryField()
    {
        await world.PublishAndIngestAsync(1,
            PublishedSamples.Statement('a', "Rent cap", official: "o1", issues: ["housing"], areas: ["housing-policy"], host: "one.gov", date: new DateOnly(2026, 3, 1)),
            PublishedSamples.Statement('b', "Rent aid", official: "o2", issues: ["transit"], areas: ["transport"], host: "two.gov", date: new DateOnly(2026, 4, 1)),
            PublishedSamples.Brief("Rent week", "Rent news."));

        async Task<string[]> Headlines(Func<SearchRequest, SearchRequest> change) =>
            [.. (await world.SearchAsync(change)).Hits.Select(hit => hit.Headline)];

        Assert.Equal(["Rent cap"], await Headlines(r => r with { Official = "o1" }));
        Assert.Equal(["Rent aid"], await Headlines(r => r with { Issue = "transit" }));
        Assert.Equal(["Rent cap"], await Headlines(r => r with { PolicyArea = "housing-policy" }));
        Assert.Equal(["Rent aid"], await Headlines(r => r with { Source = "two.gov" }));
        Assert.Equal(["Rent aid"], await Headlines(r => r with { From = new DateOnly(2026, 4, 1), To = new DateOnly(2026, 4, 30) }));
        Assert.Equal(["Rent week"], await Headlines(r => r with { Kind = PublishedRecordKind.Brief }));
        Assert.Empty(await Headlines(r => r with { Official = "o1", Issue = "transit" }));
    }

    [Fact]
    public async Task AFilterThatNeedsAnEventDateNeverMatchesARecordWithoutOne()
    {
        await world.PublishAndIngestAsync(1, PublishedSamples.Statement('a', "Rent cap", date: null));

        Assert.Empty((await world.SearchAsync(request => request with { From = new DateOnly(2000, 1, 1) })).Hits);
    }

    [Fact]
    public async Task WithoutTextTheResultsFollowTheFacetRuleOfEventDayElseTheUtcFirstPublicationDayNewestFirst()
    {
        var records = new[]
        {
            PublishedSamples.Statement('a', "Dated late", date: new DateOnly(2026, 5, 4), published: new DateTimeOffset(2026, 5, 11, 8, 0, 0, TimeSpan.Zero)),
            PublishedSamples.Statement('b', "Dated early, late publish", date: new DateOnly(2026, 5, 2), published: new DateTimeOffset(2026, 5, 11, 8, 0, 0, TimeSpan.Zero)),
            PublishedSamples.Statement('c', "Undated near UTC midnight", date: null, published: new DateTimeOffset(2026, 5, 2, 21, 0, 0, TimeSpan.FromHours(-5))),
            PublishedSamples.Statement('d', "Undated, old publish", date: null, published: new DateTimeOffset(2026, 1, 10, 8, 0, 0, TimeSpan.Zero)),
            PublishedSamples.Statement('e', "Same day, later publish, later ID", date: new DateOnly(2026, 5, 4), published: new DateTimeOffset(2026, 5, 12, 8, 0, 0, TimeSpan.Zero))
        };
        await world.PublishAndIngestAsync(1, records);

        var page = await world.SearchAsync();

        var facetOrder = ReleaseRecordOrder.NewestFirst(records.Select(record => record.Entry), ReleaseRecordOrder.Day, entry => entry.FirstPublishedAtUtc, entry => entry.RecordId);
        Assert.Equal(["e", "a", "c", "b", "d"], facetOrder.Select(entry => entry.RecordId[..1]));
        Assert.Equal(facetOrder.Select(entry => entry.RecordId), page.Hits.Select(hit => hit.RecordId));
        Assert.All(page.Hits, hit => Assert.Null(hit.Snippet));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CursorPagingWalksEveryRecordOnceInOrderWithNoRepeatAndNoGap(bool withText)
    {
        var records = Enumerable.Range(0, 7).Select(i => PublishedSamples.Statement((char)('1' + i), "Rent record " + i,
            date: i % 3 == 0 ? null : new DateOnly(2026, 3, 1 + i % 2), published: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddHours(i % 2))).ToArray();
        await world.PublishAndIngestAsync(1, records);
        var all = (await world.SearchAsync(request => request with { Text = withText ? "rent" : null, Limit = 50 })).Hits.Select(hit => hit.RecordId).ToArray();

        var walked = new List<string>();
        SearchCursor? cursor = null;
        var pages = 0;
        do
        {
            var page = await world.SearchAsync(request => request with { Text = withText ? "rent" : null, Limit = 3, Cursor = cursor });
            walked.AddRange(page.Hits.Select(hit => hit.RecordId));
            cursor = SearchCursor.Parse(page.NextCursor);
            Assert.True(++pages <= 3, "The cursor did not end the walk.");
        } while (cursor is not null);

        Assert.Equal(7, all.Length);
        Assert.Equal(all, walked);
    }

    [Theory]
    [InlineData(SearchSortKey.Date, true)]
    [InlineData(SearchSortKey.Date, false)]
    [InlineData(SearchSortKey.Published, true)]
    [InlineData(SearchSortKey.Published, false)]
    [InlineData(SearchSortKey.Headline, true)]
    [InlineData(SearchSortKey.Headline, false)]
    [InlineData(SearchSortKey.Kind, true)]
    [InlineData(SearchSortKey.Kind, false)]
    public async Task ASortedKeysetWalkVisitsEveryRecordOnceInTheOrderOfTheKeyWithTiesBrokenByRecordId(SearchSortKey sort, bool descending)
    {
        var records = SortSamples();
        await world.PublishAndIngestAsync(1, records);
        var expected = ExpectedOrder(records, sort, descending);

        var walked = new List<string>();
        SearchCursor? cursor = null;
        var pages = 0;
        do
        {
            var page = await world.SearchAsync(request => request with { Sort = sort, Descending = descending, Limit = 3, Cursor = cursor });
            walked.AddRange(page.Hits.Select(hit => hit.RecordId));
            cursor = SearchCursor.Parse(page.NextCursor);
            Assert.True(cursor is null || cursor.IsSorted);
            Assert.True(++pages <= 4, "The cursor did not end the walk.");
        } while (cursor is not null);

        Assert.Equal(expected, walked);
    }

    [Fact]
    public async Task AHeadlineSortLowercasesThenUsesCodeOrderSoCaseDoesNotMatterAndTheOrderDoesNotDependOnTheDatabaseLocale()
    {
        await world.PublishAndIngestAsync(1,
            PublishedSamples.Statement('a', "apple"), PublishedSamples.Statement('b', "Zebra"), PublishedSamples.Statement('c', "Mango"),
            PublishedSamples.Statement('d', "H.R. 2"), PublishedSamples.Statement('e', "H.R. 10"), PublishedSamples.Statement('f', "\u00E9lan"),
            PublishedSamples.Statement('2', "\u00C9lan"), PublishedSamples.Statement('1', "Apple"));

        var page = await world.SearchAsync(request => request with { Sort = SearchSortKey.Headline });

        Assert.Equal(["Apple", "apple", "H.R. 10", "H.R. 2", "Mango", "Zebra", "\u00C9lan", "\u00E9lan"], page.Hits.Select(hit => hit.Headline));
    }

    [Fact]
    public async Task ASortedSearchWithTextOnlyHoldsTheRecordsThatMatchTheTextAndKeepsTheKeysetAcrossPages()
    {
        await world.PublishAndIngestAsync(1,
            PublishedSamples.Statement('a', "Beta", source: "Xq rent cap"), PublishedSamples.Statement('b', "Alpha", source: "Xq other words"),
            PublishedSamples.Statement('c', "Gamma", source: "Xq rent relief"), PublishedSamples.Statement('d', "Delta", source: "Xq rent"));

        var first = await world.SearchAsync(request => request with { Text = "rent", Sort = SearchSortKey.Headline, Limit = 2 });
        var second = await world.SearchAsync(request => request with { Text = "rent", Sort = SearchSortKey.Headline, Limit = 2, Cursor = SearchCursor.Parse(first.NextCursor) });

        Assert.Equal(["Beta", "Delta", "Gamma"], first.Hits.Concat(second.Hits).Select(hit => hit.Headline));
        Assert.Null(second.NextCursor);
    }

    [Fact]
    public async Task ASortedSearchStillAppliesFiltersAndAPinnedReleaseToTheCursorRow()
    {
        await world.PublishAndIngestAsync(1,
            PublishedSamples.Statement('a', "Alpha", issues: ["housing"]), PublishedSamples.Statement('b', "Beta"),
            PublishedSamples.Statement('c', "Gamma", issues: ["housing"]), PublishedSamples.Statement('d', "Delta", issues: ["housing"]));
        await world.PublishAndIngestAsync(2,
            PublishedSamples.Statement('a', "Alpha", issues: ["housing"]), PublishedSamples.Statement('b', "Beta"),
            PublishedSamples.Statement('c', "Gamma", issues: ["housing"]), PublishedSamples.Statement('d', "Delta", issues: ["housing"]),
            PublishedSamples.Statement('e', "Able", issues: ["housing"]));

        var first = await world.SearchAsync(request => request with { Issue = "housing", Sort = SearchSortKey.Headline, Limit = 2 }, release: 1);
        var second = await world.SearchAsync(request => request with { Issue = "housing", Sort = SearchSortKey.Headline, Limit = 2, Cursor = SearchCursor.Parse(first.NextCursor) }, release: 1);

        Assert.Equal(["Alpha", "Delta", "Gamma"], first.Hits.Concat(second.Hits).Select(hit => hit.Headline));
    }

    [Fact]
    public async Task AForgedSortedCursorForARecordThatOnlyALaterReleaseHoldsGivesAnEmptyPageAndNoError()
    {
        await world.PublishAndIngestAsync(1, SortSamples());
        await world.PublishAndIngestAsync(2, [.. SortSamples(), PublishedSamples.Statement('9', "Only in release two")]);

        var page = await world.SearchAsync(request => request with { Sort = SearchSortKey.Headline, Cursor = SearchCursor.Sorted(1, new string('9', 64)) }, release: 1);

        Assert.Empty(page.Hits);
        Assert.Null(page.NextCursor);
    }

    [Fact]
    public async Task ADefaultOrderSearchStillGivesTheOldDatedCursor()
    {
        await world.PublishAndIngestAsync(1, SortSamples());

        var page = await world.SearchAsync(request => request with { Limit = 2 });

        Assert.StartsWith("d.1.", page.NextCursor, StringComparison.Ordinal);
    }

    private static SampleRecord[] SortSamples()
    {
        var noon = new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);
        return
        [
            PublishedSamples.Statement('1', "Zoning", date: new DateOnly(2026, 3, 2), published: noon),
            PublishedSamples.Statement('2', "Zoning", date: new DateOnly(2026, 3, 2), published: noon),
            PublishedSamples.Statement('3', "apple", date: new DateOnly(2026, 3, 2), published: noon.AddHours(1)),
            PublishedSamples.Statement('4', "Budget", date: null, published: noon),
            PublishedSamples.Statement('5', "Budget", date: new DateOnly(2026, 3, 1), published: noon),
            PublishedSamples.Statement('6', "Transit", date: new DateOnly(2026, 2, 1), published: noon.AddHours(1)),
            PublishedSamples.Statement('7', "APPLE", date: new DateOnly(2026, 3, 2), published: noon),
            PublishedSamples.DocumentChange("Zoning", "A change.", "Afterword text."),
            PublishedSamples.Vote("Budget", "A vote.", "Result: voteword")
        ];
    }

    private static string[] ExpectedOrder(SampleRecord[] records, SearchSortKey sort, bool descending)
    {
        string Key(PublishedRecordEntry entry) => sort switch
        {
            SearchSortKey.Date => ReleaseRecordOrder.Day(entry).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            SearchSortKey.Published => entry.FirstPublishedAtUtc.UtcDateTime.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            SearchSortKey.Headline => entry.Headline.ToLowerInvariant(),
            _ => PublicationProtocol.KindText(entry.Kind)
        };
        var entries = records.Select(record => record.Entry);
        var ordered = descending
            ? entries.OrderByDescending(Key, StringComparer.Ordinal).ThenByDescending(entry => entry.RecordId, StringComparer.Ordinal)
            : entries.OrderBy(Key, StringComparer.Ordinal).ThenBy(entry => entry.RecordId, StringComparer.Ordinal);
        return [.. ordered.Select(entry => entry.RecordId)];
    }

    [Fact]
    public async Task APinnedReleaseSeesTheRevisionThatReleaseHeldAndTheActiveReleaseSeesTheNewOne()
    {
        await world.PublishAndIngestAsync(1, PublishedSamples.Statement('a', "Rent cap", revision: 1));
        await world.PublishAndIngestAsync(2, PublishedSamples.Statement('a', "Rent cap revised", revision: 2));

        var pinned = Assert.Single((await world.SearchAsync(release: 1)).Hits);
        var active = Assert.Single((await world.SearchAsync()).Hits);

        Assert.Equal((1, "Rent cap", 1, 1), (pinned.RevisionNumber, pinned.Headline, pinned.FirstRelease, pinned.LastRelease));
        Assert.Equal((2, "Rent cap revised", 2, (int?)null), (active.RevisionNumber, active.Headline, active.FirstRelease, active.LastRelease));
    }

    [Fact]
    public async Task ARecordDroppedByALaterReleaseIsGoneFromTheActiveReleaseButStaysInThePinnedOne()
    {
        await world.PublishAndIngestAsync(1, PublishedSamples.Statement('a', "Rent cap"));
        await world.PublishAndIngestAsync(2, PublishedSamples.Statement('b', "Zoning"));

        Assert.Equal(["b"], (await world.SearchAsync()).Hits.Select(hit => hit.RecordId[..1]));
        Assert.Equal(["a"], (await world.SearchAsync(release: 1)).Hits.Select(hit => hit.RecordId[..1]));
    }

    [Fact]
    public async Task AReleaseThatWasNeverIngestedIsNotFoundAndTheActiveReleaseIsTheNewestOne()
    {
        await world.PublishAndIngestAsync(1, PublishedSamples.Statement('a', "Rent cap"));
        await world.PublishAndIngestAsync(3, PublishedSamples.Statement('a', "Rent cap", revision: 2));

        Assert.Null(await world.Index.FindReleaseAsync(2, CancellationToken.None));
        Assert.Equal(3, (await world.Index.FindReleaseAsync(null, CancellationToken.None))!.ReleaseNumber);
    }

    [Fact]
    public async Task AFindByIdReturnsTheRevisionOfTheGivenReleaseWithItsRecordPaths()
    {
        await world.PublishAndIngestAsync(1, PublishedSamples.Statement('a', "Rent cap", revision: 1));
        await world.PublishAndIngestAsync(2, PublishedSamples.Statement('a', "Rent cap revised", revision: 2));
        var id = new string('a', 64);

        var old = await world.Index.FindRecordAsync(id, 1, CancellationToken.None);

        Assert.Equal(1, old!.RevisionNumber);
        Assert.Equal($"records/{id}.html", old.PagePath);
        Assert.Equal($"records/{id}.json", old.DataPath);
        Assert.Null(await world.Index.FindRecordAsync(new string('b', 64), 2, CancellationToken.None));
    }
}
