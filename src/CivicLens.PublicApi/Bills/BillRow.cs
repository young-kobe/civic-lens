using CivicLens.Publication.Contracts;

namespace CivicLens.PublicApi.Bills;

/// <summary>One row of the view <c>public_api.bills</c>.</summary>
public sealed record BillRow
{
    public required string RecordId { get; init; }
    public required int RevisionNumber { get; init; }
    public required int Congress { get; init; }
    public required string BillType { get; init; }
    public required int BillNumber { get; init; }
    public required string BillTitle { get; init; }
    public required string Headline { get; init; }
    public required string Summary { get; init; }
    public required PublishedTextOrigin TextOrigin { get; init; }
    public string? Label { get; init; }
    public string? PolicyArea { get; init; }
    public string? PolicyAreaId { get; init; }
    public required DateOnly EventDate { get; init; }
    public required int SponsorCount { get; init; }
    public required int CosponsorCount { get; init; }
}
