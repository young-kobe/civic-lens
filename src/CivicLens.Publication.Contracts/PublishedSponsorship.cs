namespace CivicLens.Publication.Contracts;

public sealed record PublishedSponsorship
{
    public required PublishedOfficial Official { get; init; }
    public required PublishedSponsorshipRole Role { get; init; }
    public required bool IsOriginal { get; init; }
    public required DateOnly SponsoredOn { get; init; }
    public DateOnly? WithdrawnOn { get; init; }
    public required PublishedCitation[] Citations { get; init; }

    internal DateOnly LatestDate => WithdrawnOn is { } withdrawn && withdrawn > SponsoredOn ? withdrawn : SponsoredOn;
}
