using CivicLens.Publication.Contracts;
using CivicLens.PublicApi.Http;
using CivicLens.PublicApi.Search;
using Microsoft.AspNetCore.Http;

namespace CivicLens.PublicApi.Records;

public sealed record RelatedRequest
{
    public required PublishedRecordKind Kind { get; init; }
    public required string RecordId { get; init; }
    public RelatedCursor? Cursor { get; init; }
    public int Limit { get; init; } = SearchRequest.DefaultLimit;

    /// <summary>Reads the route and the query string. An invalid value throws <see cref="ArgumentException"/> that names the parameter. A record page lists links for the kinds that a relationship can cite, which <see cref="PublishedRelationship.IsCitedKind"/> owns.</summary>
    public static RelatedRequest Parse(string kind, string recordId, IQueryCollection query) => new()
    {
        Kind = RecordKinds.TryParse(kind, out var parsed) && PublishedRelationship.IsCitedKind(parsed)
            ? parsed
            : throw new ArgumentException("kind must be statement, action, or vote.", "kind"),
        RecordId = recordId,
        Cursor = RelatedCursor.Parse(QueryValues.Value(query, "cursor")),
        Limit = QueryValues.PageLimit(query)
    };
}
