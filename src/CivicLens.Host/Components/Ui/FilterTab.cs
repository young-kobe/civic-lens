namespace CivicLens.Host.Components.Ui;

/// <summary>A link tab goes to a page. A button tab holds a value for a script.</summary>
public sealed record FilterTab
{
    private FilterTab() { }

    public required string Label { get; init; }
    public required bool Selected { get; init; }
    public int? Count { get; init; }
    public string? Href { get; private init; }
    public string? Value { get; private init; }
    public bool IsLink => Href is not null;

    public static FilterTab Link(string label, string href, bool selected, int? count = null) =>
        new() { Label = label, Href = href, Selected = selected, Count = count };

    public static FilterTab Button(string label, string value, bool selected, int? count = null) =>
        new() { Label = label, Value = value, Selected = selected, Count = count };
}
