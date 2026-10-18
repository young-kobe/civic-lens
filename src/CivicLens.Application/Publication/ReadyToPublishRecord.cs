using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

public sealed record ReadyToPublishRecord(PublishedRecordKind Kind, string RecordId, string Headline, int RevisionNumber,
    int? PublishedRevisionNumber, bool NoLongerMatches = false)
{
    public PublishedRecordReference Reference => new(Kind, RecordId);
    public bool IsReplacement => PublishedRevisionNumber is not null;
}
