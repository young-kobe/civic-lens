using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Review;

public sealed record ApprovedRecordHit(PublishedRecordKind Kind, string RecordId, string Headline);
