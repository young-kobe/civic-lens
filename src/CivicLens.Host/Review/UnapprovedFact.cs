using CivicLens.Core.Review;
using CivicLens.Publication.Contracts;

namespace CivicLens.Host.Review;

internal sealed record UnapprovedFact(PublishedRecordKind Kind, string RecordId, string Headline, ReviewStatus Status, int OpenConcerns,
    DateTimeOffset RevisedAtUtc);
