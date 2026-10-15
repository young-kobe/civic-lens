using CivicLens.Core.Review;

namespace CivicLens.Infrastructure.Review.Persistence;

internal interface IReviewDecisionRow
{
    string DecisionId { get; }
    string RecordId { get; }
    int RevisionNumber { get; }
    int ReviewStateVersion { get; }
    ReviewDecisionKind Kind { get; }
    string DecisionJson { get; }
}
