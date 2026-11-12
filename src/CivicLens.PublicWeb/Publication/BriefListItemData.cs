namespace CivicLens.PublicWeb.Publication;

/// <summary>One weekly brief in the list. The label is the label column of the public view, and it is null for a brief that a person wrote.</summary>
public sealed record BriefListItemData(string RecordId, DateOnly WeekStart, string Headline, string? Label, DateTimeOffset? FirstPublishedAtUtc);
