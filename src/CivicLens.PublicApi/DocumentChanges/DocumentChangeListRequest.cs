using CivicLens.PublicApi.Http;
using Microsoft.AspNetCore.Http;

namespace CivicLens.PublicApi.DocumentChanges;

/// <summary>The filters of the document change list page. <c>institution</c> is the name of the office as the view holds it.</summary>
public sealed record DocumentChangeListRequest
{
    public string? Institution { get; init; }
    public string? Issue { get; init; }
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
    public DocumentChangeCursor? Cursor { get; init; }
    public required int Limit { get; init; }

    /// <summary>Reads the query string. An invalid value throws <see cref="ArgumentException"/> that names the parameter.</summary>
    public static DocumentChangeListRequest Parse(IQueryCollection query, int limit)
    {
        var request = new DocumentChangeListRequest
        {
            Institution = QueryValues.Filter(query, "institution"),
            Issue = QueryValues.Filter(query, "issue"),
            From = QueryValues.Date(query, "from"),
            To = QueryValues.Date(query, "to"),
            Cursor = DocumentChangeCursor.Parse(QueryValues.Value(query, "cursor")),
            Limit = limit
        };
        return request.From > request.To ? throw new ArgumentException("from must not be after to.", "from") : request;
    }
}
