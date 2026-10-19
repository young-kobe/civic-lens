using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Review;

public sealed record DraftLookupHit(PublishedRecordKind Kind, string RecordId);
