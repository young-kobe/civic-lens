namespace CivicLens.Publication.Contracts;

public sealed record PublishedBriefParagraph
{
    public required string Text { get; init; }
    public required string[] CitationIds { get; init; }
}
