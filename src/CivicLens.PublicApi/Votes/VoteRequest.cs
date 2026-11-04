using CivicLens.Publication.Contracts;
using CivicLens.PublicApi.Http;
using CivicLens.PublicApi.Search;
using Microsoft.AspNetCore.Http;

namespace CivicLens.PublicApi.Votes;

public sealed record VoteRequest
{
    public PublishedChamber? Chamber { get; init; }
    public int? Congress { get; init; }
    public int? Session { get; init; }
    public PublishedVoteKind? Kind { get; init; }
    /// <summary>Lower case, the stored form. A plain comparison lets the filter move below the view.</summary>
    /// <summary>The lower case form. <c>CongressBillListItem.NormalizeType</c> writes the stored form, and no database check enforces it.</summary>
    public string? BillType { get; init; }
    public int? BillNumber { get; init; }
    public string? Issue { get; init; }
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
    public VoteCursor? Cursor { get; init; }
    public int Limit { get; init; } = SearchRequest.DefaultLimit;

    /// <summary>Reads the query string. An invalid value throws <see cref="ArgumentException"/> that names the parameter.</summary>
    public static VoteRequest Parse(IQueryCollection query) => new()
    {
        Chamber = QueryValues.Choice<PublishedChamber>(query, "chamber"),
        Congress = QueryValues.Number(query, "congress", 1, int.MaxValue),
        Session = QueryValues.Number(query, "session", 1, 2),
        Kind = QueryValues.Choice<PublishedVoteKind>(query, "kind"),
        BillType = QueryValues.Filter(query, "billType")?.ToLowerInvariant(),
        BillNumber = QueryValues.Number(query, "billNumber", 1, int.MaxValue),
        Issue = QueryValues.Filter(query, "issue"),
        From = QueryValues.Date(query, "from"),
        To = QueryValues.Date(query, "to"),
        Cursor = VoteCursor.Parse(QueryValues.Value(query, "cursor")),
        Limit = QueryValues.Number(query, "limit", 1, SearchRequest.MaximumLimit) ?? SearchRequest.DefaultLimit
    };
}
