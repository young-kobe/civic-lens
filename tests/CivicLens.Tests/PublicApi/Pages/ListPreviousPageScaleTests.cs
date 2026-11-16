using System.Diagnostics;
using CivicLens.Publication.Contracts;
using CivicLens.PublicApi.Bills;
using CivicLens.PublicApi.ExecutiveActions;
using CivicLens.PublicApi.Ingest;
using CivicLens.PublicApi.Officials;
using CivicLens.PublicApi.Records;
using CivicLens.PublicApi.Search;
using CivicLens.PublicApi.Statements;
using CivicLens.PublicApi.Votes;
using CivicLens.PublicWeb.Publication;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Infrastructure.Collection;
using CivicLens.Tests.PublicApi.Votes;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Npgsql;
using Xunit.Abstractions;

namespace CivicLens.Tests.PublicApi.Pages;

/// <summary>
/// The previous page read of every keyset list at the production size of release 1096, read as <c>civic_lens_public</c> (#580 item 7).
/// Each list walks forward over its first pages and then reads the previous page of each one, which must give the page before it again.
/// A previous page read stays under 0.5 s, or costs no more than a next page read of the same list: the officials list counts the records of every official,
/// and the record page of <c>o1</c>, whom every copied bill and statement names, counts every tab. That work does not depend on the direction.
/// The seed writes no member vote rows, so the votes tab of an official is not measured here.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class ListPreviousPageScaleTests(PostgresCollection postgres, ITestOutputHelper output)
{
    private const int Pages = 6;
    private static readonly TimeSpan Budget = TimeSpan.FromMilliseconds(500);
    // The margin of timing noise between two reads of the same cost.
    private const double NextPageFactor = 1.25;

    [Fact]
    public async Task EveryPreviousPageReadOfTheProductionSizeOfRelease1096StaysUnderHalfASecondAndGivesThePageBeforeIt()
    {
        await using var world = await SearchWorld.CreateAsync(postgres, TimeSpan.FromMinutes(5));
        var templates = ProductionSizeRelease.Templates();
        await world.Site.PublishAsync(1, templates);
        await ViewsSeed.ExecuteAsync(world.OperationalDatabase,
            ProductionSizeRelease.CosponsorsSql(templates.Single(record => record.Entry.Kind == PublishedRecordKind.Action).Entry.RecordId));
        await ProductionSizeRelease.CopyAsync(world.OperationalDatabase, templates);
        await StoredBillCounts.FillAsync(world.OperationalDatabase, ProductionSizeRelease.Release, 119);
        var ingest = await world.IngestAsync();
        Assert.Equal((IngestOutcome.Ingested, ProductionSizeRelease.Release), (ingest.Outcome, ingest.ReleaseNumber));
        Assert.Empty(ingest.Failures);
        await using var views = NpgsqlDataSource.Create(world.Options.PublicViewsDatabase);
        var snapshot = new RecordSnapshot(views);
        var votes = new VoteListReader(new VoteStore(views), snapshot);
        var bills = new BillListReader(new BillStore(views), snapshot);
        var statements = new StatementListReader(new StatementStore(views), snapshot);
        var executive = new ExecutiveActionListReader(new ExecutiveActionStore(views));
        var officials = new OfficialsPageReader(new OfficialStore(views));
        var record = new OfficialRecordPageReader(views);

        (string List, Timing Slowest)[] measured =
        [
            ("votes", await MeasureAsync(async query => (await votes.ReadAsync(VoteListReader.Parse(query), default))!, data => (data.Votes.Select(row => row.RecordId), data.NextCursor, data.PreviousCursor))),
            ("bills", await MeasureAsync(async query => (await bills.ReadAsync(BillListReader.Parse(query), default))!, data => (data.Bills.Select(row => row.RecordId), data.NextCursor, data.PreviousCursor))),
            ("statements", await MeasureAsync(async query => (await statements.ReadAsync(StatementListReader.Parse(query), default))!,
                data => (data.Statements.Select(row => row.RecordId), data.NextCursor, data.PreviousCursor))),
            ("executive actions", await MeasureAsync(async query => (await executive.ReadAsync(ExecutiveActionListReader.Parse(query), default))!,
                data => (data.Actions.Select(row => row.RecordId), data.NextCursor, data.PreviousCursor))),
            ("officials", await MeasureAsync(async query => (await officials.ReadAsync(OfficialsPageReader.Parse(query), default))!,
                data => (data.Officials.Select(row => row.OfficialId), data.NextCursor, data.PreviousCursor))),
            ("o1 bills", await MeasureRecordAsync(record, OfficialRecordKind.Bills, data => data.Bills.Select(row => row.RecordId))),
            ("o1 statements", await MeasureRecordAsync(record, OfficialRecordKind.Statements, data => data.Statements.Select(row => row.RecordId))),
            ("search ranked", await MeasureSearchAsync(world, request => request with { Text = "rent" })),
            ("search by date", await MeasureSearchAsync(world, request => request)),
            ("search by headline", await MeasureSearchAsync(world, request => request with { Sort = SearchSortKey.Headline }))
        ];

        foreach (var (list, slowest) in measured)
            output.WriteLine($"{list}: slowest next page {slowest.Next.TotalMilliseconds:F0} ms, slowest previous page {slowest.Previous.TotalMilliseconds:F0} ms.");
        Assert.All(measured, item => Assert.True(item.Slowest.Previous < Budget || item.Slowest.Previous <= item.Slowest.Next * NextPageFactor,
            $"A previous page read of {item.List} took {item.Slowest.Previous}, and a next page read took {item.Slowest.Next}."));
    }

    // Walks forward over the first pages, then times the previous page read of each later page and checks that it gives the page before it.
    private static async Task<Timing> MeasureAsync<TData>(Func<IQueryCollection, Task<TData>> read, Func<TData, (IEnumerable<string> Ids, string? Next, string? Previous)> page)
    {
        async Task<KeysetWalk.Page> ReadAsync(string? cursor, bool backward)
        {
            var query = new QueryCollection(cursor is null ? [] : new Dictionary<string, StringValues> { [backward ? ListPageLink.PreviousName : ListPageLink.NextName] = cursor });
            var (ids, next, previous) = page(await read(query));
            return new([.. ids], next, previous);
        }
        return await TimeBackAsync(ReadAsync);
    }

    private static Task<Timing> MeasureRecordAsync(OfficialRecordPageReader record, OfficialRecordKind kind, Func<OfficialRecordPageData, IEnumerable<string>> ids) =>
        MeasureAsync(async query => (await record.ReadAsync("o1", OfficialRecordRequest.Parse(query) with { Kind = kind }, default))!,
            data => (ids(data), data.NextCursor, data.PreviousCursor));

    private static Task<Timing> MeasureSearchAsync(SearchWorld world, Func<SearchRequest, SearchRequest> change) => TimeBackAsync(async (cursor, backward) =>
    {
        var found = await world.SearchAsync(request => change(request) with { Limit = SearchRequest.DefaultLimit, Cursor = SearchCursor.Parse(cursor), Backward = backward });
        return new([.. found.Hits.Select(hit => hit.RecordId)], found.NextCursor, found.PreviousCursor);
    });

    private static async Task<Timing> TimeBackAsync(Func<string?, bool, Task<KeysetWalk.Page>> read)
    {
        List<KeysetWalk.Page> forward = [await read(null, false)];
        var timing = new Timing(TimeSpan.Zero, TimeSpan.Zero);
        while (forward[^1].Next is { } next && forward.Count < Pages)
        {
            var clock = Stopwatch.StartNew();
            forward.Add(await read(next, false));
            timing = timing with { Next = Max(timing.Next, clock.Elapsed) };
        }
        Assert.True(forward.Count > 1, "The list has one page, so it has no previous page to read.");
        for (var index = 1; index < forward.Count; index++)
        {
            var clock = Stopwatch.StartNew();
            var back = await read(forward[index].Previous, true);
            timing = timing with { Previous = Max(timing.Previous, clock.Elapsed) };
            Assert.Equal(forward[index - 1].Ids, back.Ids);
        }
        return timing;
    }

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;

    /// <summary>The slowest next page read and the slowest previous page read of one list.</summary>
    private sealed record Timing(TimeSpan Next, TimeSpan Previous);
}
