using System.Globalization;
using System.Text.Json;
using CivicLens.Publication.Contracts;
using CivicLens.PublicApi.Search;
using Microsoft.AspNetCore.Http;

namespace CivicLens.PublicApi.Http;

/// <summary>Reads single values from a query string. An invalid value throws <see cref="ArgumentException"/> that names the parameter.</summary>
internal static class QueryValues
{
    public static string? Value(IQueryCollection query, string name)
    {
        if (!query.TryGetValue(name, out var values)) return null;
        return values.Count == 1 ? values[0] : throw new ArgumentException($"{name} must appear once.", name);
    }

    public static int? Number(IQueryCollection query, string name, int minimum, int maximum)
    {
        var value = Value(query, name);
        if (string.IsNullOrEmpty(value)) return null;
        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number >= minimum && number <= maximum
            ? number
            : throw new ArgumentException($"{name} must be a whole number from {minimum} to {maximum}.", name);
    }

    public static int PageLimit(IQueryCollection query) => Number(query, "limit", 1, SearchRequest.MaximumLimit) ?? SearchRequest.DefaultLimit;

    public static string? Filter(IQueryCollection query, string name)
    {
        return FilterValue(Value(query, name), name);
    }

    private static string? FilterValue(string? value, string name)
    {
        if (string.IsNullOrEmpty(value)) return null;
        return value.Length <= PublishedRecordEntry.MaximumFilterValueLength
            ? value
            : throw new ArgumentException($"{name} holds at most {PublishedRecordEntry.MaximumFilterValueLength} characters.", name);
    }

    /// <summary>Reads a parameter that can repeat. Each value follows the rule of <see cref="Filter"/>. A value must not repeat. Zero values give an empty array.</summary>
    public static string[] Filters(IQueryCollection query, string name, int maximum)
    {
        if (!query.TryGetValue(name, out var values)) return [];
        if (values.Count > maximum) throw new ArgumentException($"{name} holds at most {maximum} values.", name);
        var filters = new string[values.Count];
        for (var index = 0; index < filters.Length; index++)
            filters[index] = FilterValue(values[index], name) ?? throw new ArgumentException($"{name} must not be empty.", name);
        return filters.Distinct(StringComparer.Ordinal).Count() == filters.Length ? filters : throw new ArgumentException($"{name} must not repeat a value.", name);
    }

    public static ArgumentException InvalidCursor() => new("Page cursor is not valid.", "cursor");

    public static DateOnly? Date(IQueryCollection query, string name)
    {
        var value = Value(query, name);
        if (string.IsNullOrEmpty(value)) return null;
        return DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : throw new ArgumentException($"{name} must be a date as yyyy-MM-dd.", name);
    }

    /// <summary>Reads an enum in the text form of the release files, such as <c>previousQuestion</c>.</summary>
    public static T? Choice<T>(IQueryCollection query, string name) where T : struct, Enum
    {
        var value = Value(query, name);
        if (string.IsNullOrEmpty(value)) return null;
        return ChoiceNames<T>.ByText.TryGetValue(value, out var choice)
            ? choice
            : throw new ArgumentException($"{name} must be {string.Join(", ", ChoiceNames<T>.ByText.Keys)}.", name);
    }

    private static class ChoiceNames<T> where T : struct, Enum
    {
        public static readonly Dictionary<string, T> ByText =
            Enum.GetValues<T>().ToDictionary(choice => JsonSerializer.Serialize(choice, PublicationProtocol.JsonOptions).Trim('"'), StringComparer.Ordinal);
    }
}
