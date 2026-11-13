using CivicLens.Publication.Contracts;
using CivicLens.PublicApi.Http;
using CivicLens.PublicApi.Issues;
using CivicLens.PublicApi.Officials;
using CivicLens.PublicApi.Records;
using CivicLens.PublicWeb.Publication;
using Microsoft.AspNetCore.Http;

namespace CivicLens.PublicApi.Bills;

/// <summary>Maps one page of the bill list to the presentation record of the server page. One snapshot holds the page, the facts of its bills, and the filter choices,
/// so the page comes from one release, as the statement list reads it. One bounded read gives the sponsor and the latest action of the bills on the page.
/// The page and its policy area choices cover the newest Congress of the release. It takes the bill as one typed number,
/// such as "H.R. 5334", the bill type as a choice, and the sponsor as a typed name, so it ignores the JSON Congress, type, and sponsor ID parameters.</summary>
public sealed class BillListReader(BillStore bills, RecordSnapshot snapshot)
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
            Congress = null,
            Sponsor = null,
            SponsorName = OfficialRequest.NameOf(query, "sponsor"),
            Status = QueryValues.Choice<BillStatusStage>(query, "status"),
            BillType = numberType ?? chosenType,
            BillNumber = billNumber,
            Limit = PageSize
        };
    }

    public async Task<BillListPageData?> ReadAsync(BillRequest request, CancellationToken cancellationToken)
    {
        var (connection, transaction) = await snapshot.OpenAsync(cancellationToken);
        await using var _ = connection;
        await using var __ = transaction;
        if (await bills.ListNewestCongressAsync(transaction, request, cancellationToken) is not { } list) return null;
        var page = list.Page;
        var facts = await bills.ReadListFactsAsync(transaction, [.. page.Bills.Select(bill => bill.RecordId)], cancellationToken);
        var issueChoices = (await IssueStore.ListOptionsAsync(connection, transaction, cancellationToken)).Select(issue => issue.ToChoice()).ToArray();
        var policyAreas = list.Congress is { } congress ? await bills.ListPolicyAreasAsync(transaction, congress, cancellationToken) : [];
        var rows = page.Bills.Select(bill =>
        {
            var fact = facts.GetValueOrDefault(bill.RecordId);
            return new BillListRowData(bill.RecordId, bill.Headline, bill.Label, bill.TextOrigin, bill.BillType, fact?.Sponsor, bill.EventDate, fact?.LatestActionText,
                fact?.LatestActionDate, fact?.Status);
        }).ToArray();
        var filter = RecordListFilter.Of(request.From, request.To, ("type", request.BillType), ("status", request.Status is { } status ? QueryValues.ChoiceText(status) : null),
            ("issue", request.Issue), ("policyArea", request.PolicyArea), ("sponsor", request.SponsorName), ("bill", QueryValues.BillNumberText(request.BillType, request.BillNumber)));
        return new(page.Release, list.Congress, rows, filter, page.NextCursor, Statuses, policyAreas, issueChoices);
    }

    /// <summary>The page of a refused request: the choices of the first page, the typed text of each field, and no rows.</summary>
    public async Task<BillListPageData?> ReadRefusedAsync(IQueryCollection query, string? invalid, CancellationToken cancellationToken) =>
        await ReadAsync(Parse(QueryCollection.Empty), cancellationToken) is { } data
            ? data with
            {
                Bills = [],
                NextCursor = null,
                Filter = RecordListFilter.Refused(invalid, QueryValues.Typed(query, "type", "status", "issue", "policyArea", "sponsor", "bill", "from", "to"))
            }
            : null;
}
