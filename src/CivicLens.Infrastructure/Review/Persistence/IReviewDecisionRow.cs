using CivicLens.Core.Review;

namespace CivicLens.Infrastructure.Review.Persistence;

internal interface IReviewDecisionRow
{
    string DecisionId { get; set; }
    string RecordId { get; set; }
    int RevisionNumber { get; set; }
    int ReviewStateVersion { get; set; }
    ReviewDecisionKind Kind { get; set; }
    string ActorSubject { get; set; }
    string? Note { get; set; }
    string? CorrectionNote { get; set; }
    string DecisionJson { get; set; }
    long CreatedAtUtcTicks { get; set; }
}
