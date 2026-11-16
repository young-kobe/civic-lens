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
    /// <summary>True when <see cref="Cursor"/> starts the previous page: the page reads the rows before the cursor row.</summary>
    public bool Backward { get; init; }
    public required int Limit { get; init; }

    /// <summary>Reads the query string. An invalid value throws <see cref="ArgumentException"/> that names the parameter.</summary>
    public static ExecutiveActionListRequest Parse(IQueryCollection query, int limit)
    {
        var (cursor, backward) = KeysetPage.Cursor(query);
        var (from, to) = QueryValues.DateRange(query);
        return new ExecutiveActionListRequest
        {
            Kind = QueryValues.Choice<PublishedExecutiveActionKind>(query, "type"),
            From = from,
            To = to,
            Cursor = ExecutiveActionCursor.Parse(cursor),
            Backward = backward,
            Limit = limit
        };
    }
}
