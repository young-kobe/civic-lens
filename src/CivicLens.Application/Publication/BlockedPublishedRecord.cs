using CivicLens.Core.Review;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

public sealed record BlockedPublishedRecord(PublishedRecordKind Kind, string RecordId, string Headline,
    int PublishedRevisionNumber, ReviewStatus ReviewStatus)
{
    public PublishedRecordReference Reference => new(Kind, RecordId);
}
