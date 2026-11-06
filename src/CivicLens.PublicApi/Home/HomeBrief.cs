using CivicLens.Publication.Contracts;

namespace CivicLens.PublicApi.Home;

/// <summary>The reference to the newest weekly brief of the active release: the record, the Monday that starts its week, and its headline. <c>Label</c> is <see cref="PublishedWording.ModelBrief"/> for model text and null for person text.</summary>
public sealed record HomeBrief(string RecordId, DateOnly WeekStart, string Headline, string? Label);
