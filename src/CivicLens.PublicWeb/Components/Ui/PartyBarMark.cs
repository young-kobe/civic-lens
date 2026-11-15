namespace CivicLens.PublicWeb.Components.Ui;

/// <summary>A line across a bar at a whole count. The legend shows the count, then the label that says what the count is.</summary>
public sealed record PartyBarMark(string Label, int Count);
