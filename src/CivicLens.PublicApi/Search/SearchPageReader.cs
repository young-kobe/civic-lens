using CivicLens.PublicApi.Issues;
using CivicLens.Publication.Contracts;
using CivicLens.PublicApi.Bills;
using CivicLens.PublicApi.Officials;
using CivicLens.PublicWeb.Publication;
using Microsoft.AspNetCore.Http;

namespace CivicLens.PublicApi.Search;

/// <summary>Builds the search page from one request. The index answers the results. Officials and a bill number match come only with the first page of a text search.
/// A kind, official, issue, or date filter hides the bill number match, because the filter can leave out the bill. The page shows nothing before the first search,
/// so an empty request reads no results.</summary>
public sealed class SearchPageReader(SearchIndex index, IssueStore issueStore, OfficialStore officialStore, BillStore bills)
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
        var issues = (await issueStore.ListOptionsAsync(cancellationToken)).Select(issue => new SearchChoiceData(issue.IssueId, issue.Name)).ToArray();
        var typed = Typed(request);
        if (typed.IsEmpty) return new(typed, release.ReleaseNumber, [], false, null, [], null, issues, null);

        var page = await index.SearchAsync(release.ReleaseNumber, request, cancellationToken);
        var hits = page.Hits.Select(Hit).ToArray();
        var first = request.Cursor is null && request.Text is not null;
        var (officials, moreOfficials) = first ? await FindOfficialsAsync(request.Text!, cancellationToken) : ([], false);
        var bill = first && !typed.HasFilter ? await FindBillAsync(request.Text!, hits, cancellationToken) : null;
        var filterName = page.Hits.SelectMany(hit => hit.Officials).FirstOrDefault(official => official.Id == request.Official)?.Name;
        return new(typed, release.ReleaseNumber, officials, moreOfficials, bill, hits, page.NextCursor, issues, filterName);
    }

    private static SearchQueryData Typed(SearchRequest request) => new(request.Text,
        request.Kind is { } kind ? PublicationProtocol.KindText(kind) : null, request.Official, request.Issue,
        request.From?.ToString("yyyy-MM-dd"), request.To?.ToString("yyyy-MM-dd"))
    {
        PolicyArea = request.PolicyArea,
        Source = request.Source,
        Sort = request.Sort is { } sort ? SearchSortKeys.Text(sort) : null,
        Direction = request.Sort is null ? null : request.Descending ? "desc" : "asc",
        Limit = request.Limit == SearchRequest.DefaultLimit ? null : request.Limit
    };

    private static SearchHitData Hit(RecordHit hit) => new(hit.Kind, hit.RecordId, hit.Headline, hit.EventDate, hit.BillLine, hit.Snippet,
        [.. hit.Officials.Select(official => official.Name)]);

    /// <summary>Finds officials whose name, or any word of it, starts with the text. <see cref="OfficialStore"/> owns the name match and puts an exact name first.
    /// The store reads one row past the limit, so its next cursor says that more officials match.</summary>
    private async Task<(SearchOfficialData[] Officials, bool More)> FindOfficialsAsync(string text, CancellationToken cancellationToken)
    {
        var name = text.Trim().ToLowerInvariant();
        if (name.Length < MinimumOfficialText) return ([], false);
        var found = await officialStore.ListAsync(new OfficialRequest { NamePrefix = name, ExactNameFirst = true, Limit = MaximumOfficials }, cancellationToken);
        if (found is null) return ([], false);
        return ([.. found.Officials.Select(row => new SearchOfficialData(row.OfficialId, row.Name, row.Kind, row.Office, row.State, row.District, row.Party))],
            found.NextCursor is not null);
    }

    /// <summary>Finds the newest bill with the number. Another bill with the number is named only when its record is a result on this page.</summary>
    private async Task<SearchBillData?> FindBillAsync(string text, SearchHitData[] hits, CancellationToken cancellationToken)
    {
        if (!SearchBillNumber.TryParse(text, out var billType, out var number)) return null;
        var found = await bills.ListAsync(new BillRequest { BillNumber = number, BillType = billType, Limit = BillMatchLimit }, cancellationToken);
        if (found is null || found.Bills.Length == 0) return null;
        var newest = found.Bills[0];
        var listed = found.Bills.Skip(1).Where(bill => hits.Any(hit => hit.RecordId == bill.RecordId)).ToArray();
        var facts = await bills.ReadPinFactsAsync([newest.RecordId, .. listed.Select(bill => bill.RecordId)], cancellationToken);
        var pin = facts.GetValueOrDefault(newest.RecordId);
        var others = listed.Select(bill => new SearchOtherBillData(bill.RecordId, BillNumbers.Of(bill.BillType, bill.BillNumber),
            BillNumbers.ChamberOf(bill.BillType), facts.GetValueOrDefault(bill.RecordId)?.Sponsor)).ToArray();
        return new(newest.RecordId, newest.BillLine ?? newest.Headline, newest.Congress, pin?.Sponsor, newest.PublicLaw, pin?.LawDate, pin?.Vote, others);
    }
}
