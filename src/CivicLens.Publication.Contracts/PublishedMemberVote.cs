namespace CivicLens.Publication.Contracts;

public sealed record PublishedMemberVote
{
    public required PublishedOfficial Official { get; init; }
    public required string Party { get; init; }
    public required string Cast { get; init; }
    public required PublishedCitation Citation { get; init; }
}
