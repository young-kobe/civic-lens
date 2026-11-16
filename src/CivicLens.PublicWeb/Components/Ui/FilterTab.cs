namespace CivicLens.PublicWeb.Components.Ui;

/// <summary>A tab that links to a page.</summary>
public sealed record FilterTab
{
    private FilterTab() { }

    public required string Label { get; init; }
    public required bool Selected { get; init; }
    public int? Count { get; init; }
    public required string Href { get; init; }

    public static FilterTab Link(string label, string href, bool selected, int? count = null) =>
        new() { Label = label, Href = href, Selected = selected, Count = count };
}
