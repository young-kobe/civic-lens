using CivicLens.PublicApi.Http;
using CivicLens.PublicApi.Search;
using Microsoft.AspNetCore.Http;

namespace CivicLens.PublicApi.Statements;

public sealed record StatementRequest
{
    public string? Official { get; init; }
    /// <summary>The start of a speaker name, as <see cref="Officials.OfficialRequest.NameOf"/> reads it. Only the list page sets it; the JSON query string never does.</summary>
    public string? SpeakerName { get; init; }
    public string? Issue { get; init; }
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
    public StatementCursor? Cursor { get; init; }
    public int Limit { get; init; } = SearchRequest.DefaultLimit;

    public static StatementRequest Parse(IQueryCollection query)
    {
        var request = new StatementRequest
        {
            Official = QueryValues.Filter(query, "official"),
            Issue = QueryValues.Filter(query, "issue"),
            From = QueryValues.Date(query, "from"),
            To = QueryValues.Date(query, "to"),
            Cursor = StatementCursor.Parse(QueryValues.Value(query, "cursor")),
            Limit = QueryValues.Number(query, "limit", 1, SearchRequest.MaximumLimit) ?? SearchRequest.DefaultLimit
        };
        return request.From > request.To ? throw new ArgumentException("from must not be after to.", "from") : request;
    }
}
