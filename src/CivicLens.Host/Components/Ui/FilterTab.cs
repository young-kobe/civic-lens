namespace CivicLens.Host.Components.Ui;

/// <summary>One option of a link-based filter; Selected marks the current one.</summary>
public sealed record FilterTab(string Label, string Href, bool Selected, int? Count = null);
