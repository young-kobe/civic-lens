namespace CivicLens.Publication.Contracts;

public sealed record PublishedCatalogEntry
{
    public required string Id { get; init; }
    public required string Name { get; init; }
}
