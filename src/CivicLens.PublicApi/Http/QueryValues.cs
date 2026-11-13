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

    /// <summary>The text of each named parameter as the reader typed it, in the given order, for a form that shows a refused request again. It reads the first value of a repeated parameter.</summary>
    public static (string Name, string? Value)[] Typed(IQueryCollection query, params string[] names) =>
        [.. names.Select(name => (name, query.TryGetValue(name, out var values) ? values.FirstOrDefault() : null))];

    public static int? Number(IQueryCollection query, string name, int minimum, int maximum)
    {
        var value = Value(query, name);
        if (string.IsNullOrEmpty(value)) return null;
        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number >= minimum && number <= maximum
            ? number
            : throw new ArgumentException($"{name} must be a whole number from {minimum} to {maximum}.", name);
    }

    public static int PageLimit(IQueryCollection query) => Number(query, "limit", 1, SearchRequest.MaximumLimit) ?? SearchRequest.DefaultLimit;

    public static string? Filter(IQueryCollection query, string name) => FilterValue(Value(query, name), name);

    /// <summary>Applies the filter rule to one value: empty means no filter, and a value over the maximum length throws <see cref="ArgumentException"/>.</summary>
    public static string? FilterValue(string? value, string name)
    {
        if (string.IsNullOrEmpty(value)) return null;
        return value.Length <= PublishedRecordEntry.MaximumFilterValueLength
            ? value
            : throw new ArgumentException($"{name} holds at most {PublishedRecordEntry.MaximumFilterValueLength} characters.", name);
    }

    /// <summary>Reads a bill number typed as "H.R. 5334" or "5334". An empty value is no filter. A text that is no bill number throws <see cref="ArgumentException"/>.</summary>
    public static (string? BillType, int? BillNumber) BillNumber(IQueryCollection query, string name)
    {
        if (Filter(query, name) is not { } value) return (null, null);
        return SearchBillNumber.TryParse(value, out var billType, out var number)
            ? (billType, number)
            : throw new ArgumentException($"{name} must be a bill number, such as H.R. 5334.", name);
    }

    /// <summary>The bill number as the page form types it, such as "H.R. 5334", or a bare number when the type is not known. Null when there is no number.</summary>
    public static string? BillNumberText(string? billType, int? billNumber) => billNumber is not { } number ? null
        : billType is null ? number.ToString(CultureInfo.InvariantCulture) : BillNumbers.Of(billType, number);

    /// <summary>The text form of an enum value, as <see cref="Choice{T}"/> reads it.</summary>
    public static string ChoiceText<T>(T choice) where T : struct, Enum => JsonSerializer.Serialize(choice, PublicationProtocol.JsonOptions).Trim('"');

    public static ArgumentException InvalidCursor() => new("Page cursor is not valid.", "cursor");

    public static DateOnly? Date(IQueryCollection query, string name)
    {
        var value = Value(query, name);
        if (string.IsNullOrEmpty(value)) return null;
        return DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : throw new ArgumentException($"{name} must be a date as yyyy-MM-dd.", name);
    }

    /// <summary>The one owner of the date range of a list: the from and to dates, where a from date after the to date throws <see cref="ArgumentException"/> that names from.</summary>
    public static (DateOnly? From, DateOnly? To) DateRange(IQueryCollection query)
    {
        var from = Date(query, "from");
        var to = Date(query, "to");
        return from > to ? throw new ArgumentException("from must not be after to.", "from") : (from, to);
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
            Enum.GetValues<T>().ToDictionary(ChoiceText, StringComparer.Ordinal);
    }
}
