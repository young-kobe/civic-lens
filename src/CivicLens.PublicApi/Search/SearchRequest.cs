using System.Globalization;
using CivicLens.Publication.Contracts;
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
    public SearchCursor? Cursor { get; init; }
    public int Limit { get; init; } = DefaultLimit;

    public bool IsRanked => Text is not null;

    /// <summary>Reads the query string. An invalid value throws <see cref="ArgumentException"/> that names the parameter.</summary>
    public static SearchRequest Parse(IQueryCollection query)
    {
        var cursor = SearchCursor.Parse(Value(query, "cursor"));
        var release = ReleaseNumber(query);
        if (cursor is not null && release is not null && release != cursor.Release)
            throw new ArgumentException("The cursor belongs to another release.", "cursor");
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
            Cursor = cursor,
            Limit = Number(query, "limit", 1, MaximumLimit) ?? DefaultLimit
        };
        if (cursor is not null && (cursor.Rank is not null) != request.IsRanked)
            throw new ArgumentException("The cursor does not fit this search.", "cursor");
        return request;
    }

    public static int? ReleaseNumber(IQueryCollection query) => Number(query, "release", 1, int.MaxValue);

    private static string? Value(IQueryCollection query, string name)
    {
        if (!query.TryGetValue(name, out var values)) return null;
        return values.Count == 1 ? values[0] : throw new ArgumentException($"{name} must appear once.", name);
    }

    private static string? QueryText(IQueryCollection query)
    {
        var text = Value(query, "q")?.Trim();
        if (text?.Length > MaximumQueryLength) throw new ArgumentException($"q holds at most {MaximumQueryLength} characters.", "q");
        return string.IsNullOrEmpty(text) ? null : text;
    }

    private static string? Filter(IQueryCollection query, string name)
    {
        var value = Value(query, name);
        if (string.IsNullOrEmpty(value)) return null;
        return value.Length <= PublishedRecordEntry.MaximumFilterValueLength
            ? value
            : throw new ArgumentException($"{name} holds at most {PublishedRecordEntry.MaximumFilterValueLength} characters.", name);
    }

    private static PublishedRecordKind? KindFilter(IQueryCollection query)
    {
        var value = Value(query, "kind");
        if (string.IsNullOrEmpty(value)) return null;
        return RecordKinds.TryParse(value, out var kind) ? kind : throw new ArgumentException("kind is not a record kind.", "kind");
    }

    private static DateOnly? Date(IQueryCollection query, string name)
    {
        var value = Value(query, name);
        if (string.IsNullOrEmpty(value)) return null;
        return DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : throw new ArgumentException($"{name} must be a date as yyyy-MM-dd.", name);
    }

    private static int? Number(IQueryCollection query, string name, int minimum, int maximum)
    {
        var value = Value(query, name);
        if (string.IsNullOrEmpty(value)) return null;
        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number >= minimum && number <= maximum
            ? number
            : throw new ArgumentException($"{name} must be a whole number from {minimum} to {maximum}.", name);
    }
}
