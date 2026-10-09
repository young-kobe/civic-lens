namespace CivicLens.Publication.Contracts;

public sealed record PublishedWordEdit
{
    public required int BeforeStart { get; init; }
    public required int BeforeLength { get; init; }
    public required int AfterStart { get; init; }
    public required int AfterLength { get; init; }
}
