namespace CivicLens.Publication.Contracts;

public sealed record PublishedOfficial
{
    public required string Id { get; init; }
    public required string Name { get; init; }
}
