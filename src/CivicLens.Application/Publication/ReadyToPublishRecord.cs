using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

public sealed record ReadyToPublishRecord(PublishedRecordKind Kind, string RecordId, string Headline, int RevisionNumber,
    int? PublishedRevisionNumber)
{
    public PublishedRecordReference Reference => new(Kind, RecordId);
}
