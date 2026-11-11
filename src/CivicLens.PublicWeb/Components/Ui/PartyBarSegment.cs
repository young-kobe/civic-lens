namespace CivicLens.PublicWeb.Components.Ui;

/// <summary>One labeled part of a party bar. The label names the segment in the legend and in the text description.</summary>
public sealed record PartyBarSegment(string Label, int Count, PartyTone Tone, PartyBarFill Fill);
