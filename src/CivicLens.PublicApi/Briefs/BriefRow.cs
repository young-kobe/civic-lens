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
    public required int CutoffReleaseNumber { get; init; }
    public required int CoverageConsidered { get; init; }
    public required int CoverageSentToModel { get; init; }
    /// <summary>Null when no Approve decision is stored for the revision.</summary>
    public DateTimeOffset? ApprovedAtUtc { get; init; }
    /// <summary>Null when the record has no first publication time.</summary>
    public DateTimeOffset? FirstPublishedAtUtc { get; init; }
}
