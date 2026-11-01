using CivicLens.Core.Review;

namespace CivicLens.Application.Publication;

public sealed record TakeOffSiteRequest(PublishedRecordReference Record, ReviewStatus Status, string Note, string IdempotencyKey);
