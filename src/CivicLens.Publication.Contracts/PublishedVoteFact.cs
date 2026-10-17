namespace CivicLens.Publication.Contracts;

public sealed record PublishedVoteFact
{
    public required string Text { get; init; }
    public required PublishedCitation Citation { get; init; }
}
