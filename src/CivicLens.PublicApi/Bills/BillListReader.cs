using CivicLens.Publication.Contracts;
using CivicLens.PublicApi.Http;
using CivicLens.PublicApi.Issues;
using CivicLens.PublicApi.Officials;
using CivicLens.PublicWeb.Publication;
using Microsoft.AspNetCore.Http;

namespace CivicLens.PublicApi.Bills;

/// <summary>Maps one page of the bill list to the presentation record of the server page. It reads through <see cref="BillStore"/> and <see cref="IssueStore"/>, as the JSON routes do.
/// One more bounded read gives the sponsor and the latest action of the bills on the page. The page lists the newest Congress of the release. It takes the bill as one typed number,
/// such as "H.R. 5334", the bill type as a choice, and the sponsor as a typed name, so it ignores the JSON Congress, type, and sponsor ID parameters.</summary>
public sealed class BillListReader(BillStore bills, IssueStore issues)
{
    public const int PageSize = 25;

    private static readonly SearchChoiceData[] Statuses =
        [.. Enum.GetValues<BillStatusStage>().Select(stage => new SearchChoiceData(QueryValues.ChoiceText(stage), BillStatusMeaning.StageName(stage)))];

    /// <summary>Reads the query string. An invalid value throws <see cref="ArgumentException"/> that names the parameter.</summary>
    public static BillRequest Parse(IQueryCollection query)
    {
        var (numberType, billNumber) = QueryValues.BillNumber(query, "bill");
        var chosenType = QueryValues.Filter(query, "type") is { } type
            ? BillNumbers.IsType(type) ? type : throw new ArgumentException("type must be a bill type, such as hr.", "type")
            : null;
        if (numberType is not null && chosenType is not null && numberType != chosenType)
            throw new ArgumentException("bill and type name two different bill types.", "type");
        return BillRequest.Parse(query) with
        {
            Congress = null, Sponsor = null, SponsorName = OfficialRequest.NameOf(query, "sponsor"), Status = QueryValues.Choice<BillStatusStage>(query, "status"),
            BillType = numberType ?? chosenType, BillNumber = billNumber, Limit = PageSize
        };
    }

    public async Task<BillListPageData?> ReadAsync(BillRequest request, CancellationToken cancellationToken)
    {
        if (await bills.ListNewestCongressAsync(request, cancellationToken) is not { } list) return null;
        var page = list.Page;
        var facts = await bills.ReadListFactsAsync([.. page.Bills.Select(bill => bill.RecordId)], cancellationToken);
        var issueChoices = (await issues.ListOptionsAsync(cancellationToken)).Select(issue => issue.ToChoice()).ToArray();
        var rows = page.Bills.Select(bill =>
        {
            var fact = facts.GetValueOrDefault(bill.RecordId);
            return new BillListRowData(bill.RecordId, bill.Headline, bill.Label, bill.BillType, fact?.Sponsor, bill.EventDate, fact?.LatestActionText, fact?.LatestActionDate,
                fact?.Status);
        }).ToArray();
        var filter = RecordListFilter.Of(request.From, request.To, ("type", request.BillType), ("status", request.Status is { } status ? QueryValues.ChoiceText(status) : null),
            ("issue", request.Issue), ("policyArea", request.PolicyArea), ("sponsor", request.SponsorName), ("bill", QueryValues.BillNumberText(request.BillType, request.BillNumber)));
        return new(page.Release, list.Congress, list.BillsInCongress, rows, filter, page.NextCursor, Statuses, await bills.ListPolicyAreasAsync(cancellationToken), issueChoices);
    }
}
