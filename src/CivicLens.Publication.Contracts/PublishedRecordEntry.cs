namespace CivicLens.Publication.Contracts;

public sealed record PublishedRecordEntry
{
    public required string RecordId { get; init; }
    public required int RevisionNumber { get; init; }
    public required string Headline { get; init; }
    public required DateTimeOffset FirstPublishedAtUtc { get; init; }

    public void Validate()
    {
        if (!PublicationProtocol.IsRecordId(RecordId) || RevisionNumber < 1 || string.IsNullOrWhiteSpace(Headline))
            throw new InvalidDataException("Release manifest entry is invalid.");
    }
}
