using CivicLens.Publication.Contracts;

namespace CivicLens.PublicApi.Issues;

/// <summary>One bill on an issue. <c>TextOrigin</c> says who wrote <c>Label</c>. <c>Sponsor</c> is the name of the first sponsor in stored order, or null when the release names no sponsor.
/// <c>LatestActionText</c> and <c>LatestActionDate</c> are the stored status of the active release, or null when the release stores none.</summary>
public sealed record IssueBill(string RecordId, string Headline, string? Label, PublishedTextOrigin TextOrigin, string BillType, string? BillLine, string? Sponsor,
    string? LatestActionText, DateOnly? LatestActionDate);
