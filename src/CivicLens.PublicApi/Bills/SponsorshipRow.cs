using CivicLens.Publication.Contracts;

namespace CivicLens.PublicApi.Bills;

/// <summary>One row of the view <c>public_api.bill_sponsorships</c>. The official ID is the configured ID that the release files use.</summary>
public sealed record SponsorshipRow
{
    public required int Position { get; init; }
    public required string OfficialId { get; init; }
    public required PublishedSponsorshipRole Role { get; init; }
    public required bool IsOriginal { get; init; }
    public required DateOnly SponsoredOn { get; init; }
    public DateOnly? WithdrawnOn { get; init; }
}
