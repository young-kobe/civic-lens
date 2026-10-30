namespace CivicLens.Publication.Contracts;

public sealed record PublishedExecutiveActionFact
{
    public required string Label { get; init; }
    public required string Value { get; init; }
    public required PublishedCitation Citation { get; init; }
}
