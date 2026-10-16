namespace CivicLens.Publication.Contracts;

public sealed record PublishedBriefCoverage
{
    public required int Considered { get; init; }
    public required int SentToModel { get; init; }
    public required int Cited { get; init; }
}
