using System.Globalization;
using CivicLens.Publication.Contracts;
using CivicLens.PublicApi.Http;
using Microsoft.AspNetCore.Http;

namespace CivicLens.PublicApi.Search;

public sealed record SearchRequest
{
    public const int MaximumQueryLength = 200;
    public const int DefaultLimit = 20;
    public const int MaximumLimit = 50;

    public string? Text { get; init; }
    public PublishedRecordKind? Kind { get; init; }
    public string? Official { get; init; }
    public string? Issue { get; init; }
    public string? PolicyArea { get; init; }
    public string? Source { get; init; }
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
    public int? Release { get; init; }
    public SearchSortKey? Sort { get; init; }
    public bool Descending { get; init; }
    public SearchCursor? Cursor { get; init; }
    public int Limit { get; init; } = DefaultLimit;

    /// <summary>Text ranks the results unless the request names a sort.</summary>
    public bool IsRanked => Text is not null && Sort is null;

    /// <summary>Reads the query string. An invalid value throws <see cref="ArgumentException"/> that names the parameter.</summary>
    public static SearchRequest Parse(IQueryCollection query)
    {
        var cursor = SearchCursor.Parse(QueryValues.Value(query, "cursor"));
        var release = ReleaseNumber(query);
        if (cursor is not null && release is not null && release != cursor.Release)
            throw new ArgumentException("The cursor belongs to another release.", "cursor");
        var (sort, descending) = SortOrder(query);
        var request = new SearchRequest
        {
            Text = QueryText(query),
            Kind = KindFilter(query),
            Official = Filter(query, "official"),
            Issue = Filter(query, "issue"),
            PolicyArea = Filter(query, "policyArea"),
            Source = Filter(query, "source"),
            From = Date(query, "from"),
            To = Date(query, "to"),
            Release = release ?? cursor?.Release,
            Sort = sort,
            Descending = descending,
            Cursor = cursor,
            Limit = QueryValues.Number(query, "limit", 1, MaximumLimit) ?? DefaultLimit
        };
        if (cursor is not null && !request.Accepts(cursor))
            throw new ArgumentException("The cursor does not fit this search.", "cursor");
        return request;
    }

    private bool Accepts(SearchCursor cursor) => IsRanked ? cursor.Rank is not null : Sort is null ? cursor.SortDate is not null : cursor.IsSorted;

    public static int? ReleaseNumber(IQueryCollection query) => QueryValues.Number(query, "release", 1, int.MaxValue);

    private static (SearchSortKey? Sort, bool Descending) SortOrder(IQueryCollection query)
    {
        var direction = QueryValues.Value(query, "direction");
        if (direction is not (null or "" or "asc" or "desc")) throw new ArgumentException("direction must be asc or desc.", "direction");
        var name = QueryValues.Value(query, "sort");
        if (string.IsNullOrEmpty(name))
            return string.IsNullOrEmpty(direction) ? (null, false) : throw new ArgumentException("direction needs sort.", "direction");
        if (!SearchSortKeys.TryParse(name, out var key)) throw new ArgumentException("sort must be date, published, headline, or kind.", "sort");
        return (key, string.IsNullOrEmpty(direction) ? SearchSortKeys.DefaultsToDescending(key) : direction == "desc");
    }

    private static string? QueryText(IQueryCollection query)
    {
        var text = QueryValues.Value(query, "q")?.Trim();
        if (text?.Length > MaximumQueryLength) throw new ArgumentException($"q holds at most {MaximumQueryLength} characters.", "q");
        return string.IsNullOrEmpty(text) ? null : text;
    }

    private static string? Filter(IQueryCollection query, string name)
    {
        var value = QueryValues.Value(query, name);
        if (string.IsNullOrEmpty(value)) return null;
        return value.Length <= PublishedRecordEntry.MaximumFilterValueLength
            ? value
            : throw new ArgumentException($"{name} holds at most {PublishedRecordEntry.MaximumFilterValueLength} characters.", name);
    }

    private static PublishedRecordKind? KindFilter(IQueryCollection query)
    {
        var value = QueryValues.Value(query, "kind");
        if (string.IsNullOrEmpty(value)) return null;
        return RecordKinds.TryParse(value, out var kind) ? kind : throw new ArgumentException("kind is not a record kind.", "kind");
    }

    private static DateOnly? Date(IQueryCollection query, string name)
    {
        var value = QueryValues.Value(query, name);
        if (string.IsNullOrEmpty(value)) return null;
        return DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : throw new ArgumentException($"{name} must be a date as yyyy-MM-dd.", name);
    }
}
