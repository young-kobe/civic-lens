namespace CivicLens.Publication.Contracts;

public sealed record PublishedDocumentVersion
{
    public required string ExtractionId { get; init; }
    public required string Url { get; init; }
    public required DateTimeOffset ObservedAtUtc { get; init; }
    public required string Text { get; init; }
}
