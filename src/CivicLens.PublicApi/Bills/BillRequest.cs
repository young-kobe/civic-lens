using CivicLens.Publication.Contracts;
using CivicLens.PublicApi.Http;
using CivicLens.PublicApi.Search;
using Microsoft.AspNetCore.Http;

namespace CivicLens.PublicApi.Bills;

public sealed record BillRequest
{
    public int? Congress { get; init; }
    /// <summary>The lower case form. <c>CongressBillListItem.NormalizeType</c> writes the stored form, and no database check enforces it.</summary>
    public string? BillType { get; init; }
    public int? BillNumber { get; init; }
    public string? Sponsor { get; init; }
    /// <summary>The start of a sponsor name, as <see cref="Officials.OfficialRequest.NameOf"/> reads it. Only the list page sets it; the JSON query string never does.</summary>
    public string? SponsorName { get; init; }
    /// <summary>The stored status of the bill, as <see cref="BillStatusMeaning.StageOf"/> wrote it. Only the list page sets it; the JSON query string never does.</summary>
    public BillStatusStage? Status { get; init; }
    public string? Issue { get; init; }
    public string? PolicyArea { get; init; }
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
    public BillCursor? Cursor { get; init; }
    public int Limit { get; init; } = SearchRequest.DefaultLimit;

    /// <summary>Reads the query string. An invalid value throws <see cref="ArgumentException"/> that names the parameter.</summary>
    public static BillRequest Parse(IQueryCollection query)
    {
        var request = new BillRequest
        {
            Congress = QueryValues.Number(query, "congress", 1, int.MaxValue),
            BillType = QueryValues.Filter(query, "billType")?.ToLowerInvariant(),
            BillNumber = QueryValues.Number(query, "billNumber", 1, int.MaxValue),
            Sponsor = QueryValues.Filter(query, "sponsor"),
            Issue = QueryValues.Filter(query, "issue"),
            PolicyArea = QueryValues.Filter(query, "policyArea"),
            From = QueryValues.Date(query, "from"),
            To = QueryValues.Date(query, "to"),
            Cursor = BillCursor.Parse(QueryValues.Value(query, "cursor")),
            Limit = QueryValues.Number(query, "limit", 1, SearchRequest.MaximumLimit) ?? SearchRequest.DefaultLimit
        };
        return request.From > request.To ? throw new ArgumentException("from must not be after to.", "from") : request;
    }
}
