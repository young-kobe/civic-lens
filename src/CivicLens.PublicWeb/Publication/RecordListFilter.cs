using System.Globalization;
using CivicLens.PublicWeb.Components.Ui;

namespace CivicLens.PublicWeb.Publication;

/// <summary>The filters of one list request, as the reader typed them. Each value is keyed by its query parameter name. An empty value is no filter and is not kept.
/// The order of the values is the order of the form fields, and the dates come last. <see cref="From"/> and <see cref="To"/> are the date filters.</summary>
public sealed record RecordListFilter(IReadOnlyDictionary<string, string> Values, DateOnly? From, DateOnly? To)
{
    /// <summary>Keeps the pairs that have a value, in the given order, and then the dates.</summary>
    public static RecordListFilter Of(DateOnly? from, DateOnly? to, params (string Name, string? Value)[] pairs) =>
        new(pairs.Append((Name: "from", Value: DateText(from))).Append((Name: "to", Value: DateText(to))).Where(pair => pair.Value is not null)
            .ToDictionary(pair => pair.Name, pair => pair.Value!), from, to);

    public bool IsActive => Values.Count > 0;

    public string? Value(string name) => Values.GetValueOrDefault(name);

    /// <summary>The chip of the date filters, or null when there is none. The words are the approved date chips of the lists.</summary>
    public string? DateChip => (From, To) switch
    {
        ({ } from, { } to) => $"From {Moment.Day(from)} to {Moment.Day(to)}",
        ({ } from, null) => $"On or after {Moment.Day(from)}",
        (null, { } to) => $"On or before {Moment.Day(to)}",
        _ => null
    };

    /// <summary>The query string of the same filters, with the cursor of the next page when one is given. It starts with a question mark.</summary>
    public string Query(string? cursor = null) => "?" + string.Join('&', Values.Select(pair => $"{pair.Key}={Uri.EscapeDataString(pair.Value)}")
        .Concat(cursor is null ? [] : [$"cursor={Uri.EscapeDataString(cursor)}"]));

    private static string? DateText(DateOnly? date) => date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
