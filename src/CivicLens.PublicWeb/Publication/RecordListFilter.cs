namespace CivicLens.PublicWeb.Publication;

/// <summary>The filters of one list request, as the reader typed them. Each value is keyed by its query parameter name. An empty value is no filter and is not kept.
/// The order of the values is the order of the form fields.</summary>
public sealed record RecordListFilter(IReadOnlyDictionary<string, string> Values)
{
    public static readonly RecordListFilter None = new(new Dictionary<string, string>());

    /// <summary>Keeps the pairs that have a value, in the given order.</summary>
    public static RecordListFilter Of(params (string Name, string? Value)[] pairs) =>
        new(pairs.Where(pair => pair.Value is not null).ToDictionary(pair => pair.Name, pair => pair.Value!));

    public bool IsActive => Values.Count > 0;

    public string? Value(string name) => Values.GetValueOrDefault(name);

    /// <summary>The query string of the same filters, with the cursor of the next page when one is given. It starts with a question mark.</summary>
    public string Query(string? cursor = null) => "?" + string.Join('&', Values.Select(pair => $"{pair.Key}={Uri.EscapeDataString(pair.Value)}")
        .Concat(cursor is null ? [] : [$"cursor={Uri.EscapeDataString(cursor)}"]));
}
