using CivicLens.Publication.Contracts;

namespace CivicLens.PublicApi.Briefs;

public sealed record BriefRow
{
    public required string RecordId { get; init; }
    public required int RevisionNumber { get; init; }
    public required DateOnly WeekStart { get; init; }
    public required string Headline { get; init; }
    public required PublishedTextOrigin TextOrigin { get; init; }
    public string? Label { get; init; }
}
