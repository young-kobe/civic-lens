namespace CivicLens.PublicApi.Home;

/// <summary>The reference to the newest weekly brief of the active release: the record, the Monday that starts its week, and its headline with the label of its text origin.</summary>
public sealed record HomeBrief(string RecordId, DateOnly WeekStart, string Headline, string? Label);
