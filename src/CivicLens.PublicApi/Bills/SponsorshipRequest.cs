using CivicLens.Publication.Contracts;
using CivicLens.PublicApi.Http;
using CivicLens.PublicApi.Search;
using Microsoft.AspNetCore.Http;

namespace CivicLens.PublicApi.Bills;

public sealed record SponsorshipRequest
{
    public required string RecordId { get; init; }
    public PublishedSponsorshipRole? Role { get; init; }
    public SponsorshipCursor? Cursor { get; init; }
    public int Limit { get; init; } = SearchRequest.DefaultLimit;

    /// <summary>Reads the query string. An invalid value throws <see cref="ArgumentException"/> that names the parameter.</summary>
    public static SponsorshipRequest Parse(string recordId, IQueryCollection query) => new()
    {
        RecordId = recordId,
        Role = QueryValues.Choice<PublishedSponsorshipRole>(query, "role"),
        Cursor = SponsorshipCursor.Parse(QueryValues.Value(query, "cursor")),
        Limit = QueryValues.Number(query, "limit", 1, SearchRequest.MaximumLimit) ?? SearchRequest.DefaultLimit
    };
}
