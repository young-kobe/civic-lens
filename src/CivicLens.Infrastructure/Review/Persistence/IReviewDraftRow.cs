using CivicLens.Core.Review;

namespace CivicLens.Infrastructure.Review.Persistence;

internal interface IReviewDraftRow
{
    string RecordId { get; }
    int CurrentRevisionNumber { get; set; }
    int ReviewStateVersion { get; set; }
    ReviewStatus ReviewStatus { get; set; }
    long CreatedAtUtcTicks { get; }
    long LastRevisedAtUtcTicks { get; set; }
}
