using CivicLens.Publication.Contracts;
using CivicLens.PublicApi.Bills;
using CivicLens.PublicApi.Officials;
using CivicLens.PublicWeb.Publication;
using Microsoft.AspNetCore.Http;

namespace CivicLens.PublicApi.Search;

/// <summary>Builds the search page from one request. The index answers the results. Officials and a bill number match come only with the first page of a text search.
/// The page shows nothing before the first search, so an empty request reads no results.</summary>
public sealed class SearchPageReader(SearchIndex index, SearchPageStore pages, OfficialStore officialStore, BillStore bills)
{
    private const int BillMatchLimit = 5;

    /// <summary>The most officials that the page shows above the results.</summary>
    public const int MaximumOfficials = 3;

    /// <summary>A shorter query would match too many names.</summary>
    public const int MinimumOfficialText = 3;

    public async Task<SearchPageData?> ReadAsync(IQueryCollection query, CancellationToken cancellationToken)
    {
        var request = SearchRequest.Parse(query);
        if (await index.FindReleaseAsync(request.Release, cancellationToken) is not { } release) return null;
        var issues = await pages.ListIssuesAsync(cancellationToken);
        var typed = Typed(request);
        if (typed.IsEmpty) return new(typed, release.ReleaseNumber, [], null, [], null, issues);

        var page = await index.SearchAsync(release.ReleaseNumber, request, cancellationToken);
        var hits = page.Hits.Select(Hit).ToArray();
        var first = request.Cursor is null && request.Text is not null;
        var officials = first ? await FindOfficialsAsync(request.Text!, cancellationToken) : [];
        var bill = first ? await FindBillAsync(request.Text!, cancellationToken) : null;
        return new(typed, release.ReleaseNumber, officials, bill, hits, page.NextCursor, issues);
    }

    private static SearchQueryData Typed(SearchRequest request) => new(request.Text,
        request.Kind is { } kind ? PublicationProtocol.KindText(kind) : null, request.Official, request.Issue,
        request.From?.ToString("yyyy-MM-dd"), request.To?.ToString("yyyy-MM-dd"));

    private static SearchHitData Hit(RecordHit hit) => new(hit.Kind, hit.RecordId, hit.Headline, hit.EventDate, hit.BillLine, hit.Snippet,
        [.. hit.Officials.Select(official => official.Name)]);

    /// <summary>Finds officials whose name, or any word of it, starts with the text. <see cref="OfficialStore"/> owns the name match.</summary>
    private async Task<SearchOfficialData[]> FindOfficialsAsync(string text, CancellationToken cancellationToken)
    {
        var name = text.Trim().ToLowerInvariant();
        if (name.Length < MinimumOfficialText) return [];
        var found = await officialStore.ListAsync(new OfficialRequest { NamePrefix = name, Limit = MaximumOfficials }, cancellationToken);
        return found is null ? [] : [.. found.Officials.Select(row => new SearchOfficialData(row.OfficialId, row.Name, row.Kind, row.State, row.District, row.Party))];
    }

    private async Task<SearchBillData?> FindBillAsync(string text, CancellationToken cancellationToken)
    {
        if (!SearchBillNumber.TryParse(text, out var billType, out var number)) return null;
        var found = await bills.ListAsync(new BillRequest { BillNumber = number, BillType = billType, Limit = BillMatchLimit }, cancellationToken);
        if (found is null || found.Bills.Length == 0) return null;
        var newest = found.Bills[0];
        var others = found.Bills.Length - 1 + (found.NextCursor is null ? 0 : 1);
        return new(newest.RecordId, newest.BillLine ?? newest.Headline, newest.Congress, newest.PublicLaw, others);
    }
}
