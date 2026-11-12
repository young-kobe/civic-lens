using CivicLens.Publication.Contracts;
using CivicLens.PublicApi.Http;
using CivicLens.PublicApi.Search;
using Microsoft.AspNetCore.Http;

namespace CivicLens.PublicApi.Officials;

public sealed record OfficialRequest
{
    public const int MaximumNameLength = 100;

    /// <summary>The term kind that the chamber filter names: <c>house</c> is a Representative, <c>senate</c> is a Senator, and <c>executive</c> is an Executive.</summary>
    public PublishedOfficeKind? Kind { get; init; }
    /// <summary>Upper case, as the terms store the two letter code.</summary>
    public string? State { get; init; }
    /// <summary>Lower case. The query compares it with the lower case party word.</summary>
    public string? Party { get; init; }
    /// <summary>Lower case. The query matches the start of the lower case name, or the start of any word after a space in it.</summary>
    public string? NamePrefix { get; init; }
    /// <summary>True puts an official whose whole name equals <see cref="NamePrefix"/> before the rest. It needs a name and no cursor, because a cursor does not carry that order. The query string never sets it.</summary>
    public bool ExactNameFirst { get; init; }
    public OfficialCursor? Cursor { get; init; }
    public int Limit { get; init; } = SearchRequest.DefaultLimit;

    /// <summary>Reads the query string. An invalid value throws <see cref="ArgumentException"/> that names the parameter.</summary>
    public static OfficialRequest Parse(IQueryCollection query) => new()
    {
        Kind = Chamber(QueryValues.Value(query, "chamber")),
        State = QueryValues.Filter(query, "state")?.ToUpperInvariant(),
        Party = QueryValues.Filter(query, "party")?.ToLowerInvariant(),
        NamePrefix = Prefix(QueryValues.Value(query, "name")),
        Cursor = OfficialCursor.Parse(QueryValues.Value(query, "cursor")),
        Limit = QueryValues.PageLimit(query)
    };

    private static PublishedOfficeKind? Chamber(string? value) => string.IsNullOrEmpty(value) ? null : value switch
    {
        "house" => PublishedOfficeKind.Representative,
        "senate" => PublishedOfficeKind.Senator,
        "executive" => PublishedOfficeKind.Executive,
        _ => throw new ArgumentException("chamber must be house, senate, executive.", "chamber")
    };

    private static string? Prefix(string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        return value.Length <= MaximumNameLength ? value.ToLowerInvariant() : throw new ArgumentException($"name holds at most {MaximumNameLength} characters.", "name");
    }
}
