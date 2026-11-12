using CivicLens.PublicApi.Http;
using CivicLens.PublicApi.Issues;
using CivicLens.PublicWeb.Publication;
using Microsoft.AspNetCore.Http;

namespace CivicLens.PublicApi.Bills;

/// <summary>Maps one page of the bill list to the presentation record of the server page. It reads through <see cref="BillStore"/> and <see cref="IssueStore"/>, as the JSON routes do.
/// One more bounded read gives the sponsor and the latest action of the bills on the page. The page takes the bill as one typed number, such as "H.R. 5334",
/// and it has no sponsor filter, so it ignores the JSON sponsor parameter.</summary>
public sealed class BillListReader(BillStore bills, IssueStore issues)
{
    public const int PageSize = 25;

    /// <summary>Reads the query string. An invalid value throws <see cref="ArgumentException"/> that names the parameter.</summary>
    public static BillRequest Parse(IQueryCollection query)
    {
        var (billType, billNumber) = QueryValues.BillNumber(query, "bill");
        return BillRequest.Parse(query) with { Congress = null, Sponsor = null, BillType = billType, BillNumber = billNumber, Limit = PageSize };
    }

    public async Task<BillListPageData?> ReadAsync(BillRequest request, CancellationToken cancellationToken)
    {
        if (await bills.ListAsync(request, cancellationToken) is not { } page) return null;
        var facts = await bills.ReadListFactsAsync([.. page.Bills.Select(bill => bill.RecordId)], cancellationToken);
        var issueChoices = (await issues.ListOptionsAsync(cancellationToken)).Select(issue => issue.ToChoice()).ToArray();
        var rows = page.Bills.Select(bill =>
        {
            var fact = facts.GetValueOrDefault(bill.RecordId);
            return new BillListRowData(bill.RecordId, bill.Headline, bill.Label, bill.BillType, fact?.Sponsor, bill.EventDate, fact?.LatestActionText, fact?.LatestActionDate);
        }).ToArray();
        var filter = RecordListFilter.Of(("issue", request.Issue), ("policyArea", request.PolicyArea),
            ("bill", QueryValues.BillNumberText(request.BillType, request.BillNumber)),
            ("from", request.From?.ToString("yyyy-MM-dd")), ("to", request.To?.ToString("yyyy-MM-dd")));
        return new(page.Release, rows, filter, page.NextCursor, await bills.ListPolicyAreasAsync(cancellationToken), issueChoices);
    }
}
