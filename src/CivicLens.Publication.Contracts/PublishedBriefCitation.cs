namespace CivicLens.Publication.Contracts;

public sealed record PublishedBriefCitation
{
    public required string Id { get; init; }
    public required PublishedRecordKind Kind { get; init; }
    public required string RecordId { get; init; }
    public required int RevisionNumber { get; init; }
    public required string Headline { get; init; }
}
