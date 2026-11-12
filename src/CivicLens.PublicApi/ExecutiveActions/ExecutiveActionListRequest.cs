using CivicLens.Publication.Contracts;
using CivicLens.PublicApi.Http;
using Microsoft.AspNetCore.Http;

namespace CivicLens.PublicApi.ExecutiveActions;

/// <summary>The filters of the executive action list page. <c>type</c> is the text form of the document type, such as <c>executiveOrder</c>.</summary>
public sealed record ExecutiveActionListRequest
{
    public PublishedExecutiveActionKind? Kind { get; init; }
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
    public ExecutiveActionCursor? Cursor { get; init; }
    public required int Limit { get; init; }

    /// <summary>Reads the query string. An invalid value throws <see cref="ArgumentException"/> that names the parameter.</summary>
    public static ExecutiveActionListRequest Parse(IQueryCollection query, int limit)
    {
        var request = new ExecutiveActionListRequest
        {
            Kind = QueryValues.Choice<PublishedExecutiveActionKind>(query, "type"),
            From = QueryValues.Date(query, "from"),
            To = QueryValues.Date(query, "to"),
            Cursor = ExecutiveActionCursor.Parse(QueryValues.Value(query, "cursor")),
            Limit = limit
        };
        return request.From > request.To ? throw new ArgumentException("from must not be after to.", "from") : request;
    }
}
