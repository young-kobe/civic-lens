namespace CivicLens.Host.Components.Layout;

/// <summary>One breadcrumb step. The last step is the current page and has no link.</summary>
public sealed record Crumb(string Text, string? Href = null);
