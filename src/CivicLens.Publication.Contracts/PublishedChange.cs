namespace CivicLens.Publication.Contracts;

public sealed record PublishedChange
{
    public required int BeforeStart { get; init; }
    public required int BeforeLength { get; init; }
    public required int AfterStart { get; init; }
    public required int AfterLength { get; init; }
    public required int BeforeContextStart { get; init; }
    public required int BeforeContextLength { get; init; }
    public required int AfterContextStart { get; init; }
    public required int AfterContextLength { get; init; }
    public required PublishedWordEdit[] WordEdits { get; init; }
}
