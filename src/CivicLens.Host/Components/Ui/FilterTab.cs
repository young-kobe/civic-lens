namespace CivicLens.Host.Components.Ui;

/// <summary>A tab with an Href is a link. A tab with no Href is a button that holds its Value for a script.</summary>
public sealed record FilterTab(string Label, string? Href, bool Selected, int? Count = null, string? Value = null);
