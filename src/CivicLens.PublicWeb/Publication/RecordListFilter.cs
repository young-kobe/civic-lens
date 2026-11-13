using System.Globalization;
using CivicLens.PublicWeb.Components.Ui;
using CivicLens.Publication.Contracts;

namespace CivicLens.PublicWeb.Publication;

/// <summary>The filters of one list request, as the reader typed them. Each value is keyed by its query parameter name. An empty value is no filter and is not kept.
/// The order of the values is the order of the form fields, and the dates come last. <see cref="From"/> and <see cref="To"/> are the date filters.
/// <see cref="Invalid"/> names the field that the server refused, or is null. A refused filter keeps the typed text of each field, so the form shows it again.</summary>
public sealed record RecordListFilter(IReadOnlyDictionary<string, string> Values, DateOnly? From, DateOnly? To, string? Invalid = null)
{
    /// <summary>The approved error of a date field whose text is no date (#518).</summary>
    public const string DateError = "Enter the date as year, month and day, for example 2026-09-01.";

    /// <summary>The approved error of a From date after the To date (#525). The request refuses the From field.</summary>
    public const string DateOrderError = "The To date must be on or after the From date.";

    /// <summary>The approved error of a bill number field whose text is no bill number (#525).</summary>
    public const string BillNumberError = "Enter a bill number, such as 5334 or H.R. 5334.";

    /// <summary>The approved error of a bill type choice that differs from the type of the typed bill number (#525).</summary>
    public const string BillTypeError = "The bill number and the bill type name two different types.";

    private const string DateFormat = "yyyy-MM-dd";

    /// <summary>Keeps the pairs that have a value, in the given order, and then the dates.</summary>
    public static RecordListFilter Of(DateOnly? from, DateOnly? to, params (string Name, string? Value)[] pairs) =>
        new(pairs.Append((Name: "from", Value: DateText(from))).Append((Name: "to", Value: DateText(to))).Where(pair => pair.Value is not null)
            .ToDictionary(pair => pair.Name, pair => pair.Value!), from, to);

    /// <summary>The typed text of each field of a refused request, in form order, and the field that the server refused. It holds no dates, so it shows no date chip.</summary>
    public static RecordListFilter Refused(string? invalid, params (string Name, string? Value)[] pairs) =>
        new(pairs.Where(pair => !string.IsNullOrEmpty(pair.Value)).ToDictionary(pair => pair.Name, pair => pair.Value!), null, null, invalid);

    public bool IsActive => Values.Count > 0;

    public bool IsInvalid(string name) => Invalid == name;

    /// <summary>The error text of a refused field, or null. A date that is no date, a From date after the To date, a text that is no bill number,
    /// and a bill type that differs from the typed bill number have approved text. Any other refused field is marked invalid with no text.</summary>
    public string? ErrorOf(string name) => !IsInvalid(name) ? null : name switch
    {
        "from" or "to" when !IsDate(Value(name)) => DateError,
        "from" when IsDate(Value("to")) => DateOrderError,
        "bill" => BillNumberError,
        // A known bill type is refused only when it differs from the type of the typed bill number.
        "type" when Value("type") is { } type && BillNumbers.IsType(type) => BillTypeError,
        _ => null
    };

    /// <summary>The attributes that tie an input to its error: <c>aria-invalid</c> on the refused field, and <c>aria-describedby</c> when the field has error text.</summary>
    public IReadOnlyDictionary<string, object> InputAttributes(string name, string inputId) => !IsInvalid(name)
        ? new Dictionary<string, object>()
        : ErrorOf(name) is null
            ? new Dictionary<string, object> { ["aria-invalid"] = "true" }
            : new Dictionary<string, object> { ["aria-invalid"] = "true", ["aria-describedby"] = Field.ErrorIdOf(inputId) };

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

    private static string? DateText(DateOnly? date) => date?.ToString(DateFormat, CultureInfo.InvariantCulture);

    private static bool IsDate(string? text) => DateOnly.TryParseExact(text, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
}
