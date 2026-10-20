namespace CivicLens.Application.Collection;

public sealed record PersonConfiguration
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public DatedNameConfiguration[]? Names { get; init; }
    public string? BioguideId { get; init; }
    public string? WikidataId { get; init; }
}
