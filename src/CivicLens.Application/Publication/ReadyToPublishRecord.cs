using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

public sealed record ReadyToPublishRecord(PublishedRecordKind Kind, string RecordId, int RevisionNumber)
{
    public PublishedRecordReference Reference => new(Kind, RecordId);
}
