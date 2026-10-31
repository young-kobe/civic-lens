using CivicLens.Core.Review;
using CivicLens.Infrastructure.Review.Persistence;

namespace CivicLens.Infrastructure.Briefs.Persistence;

internal sealed class BriefReviewDecisionRow : IReviewDecisionRow
{
    public string DecisionId { get; set; } = string.Empty;
    public string RecordId { get; set; } = string.Empty;
    public int RevisionNumber { get; set; }
    public int ReviewStateVersion { get; set; }
    public string DecisionJson { get; set; } = string.Empty;
    public ReviewDecisionKind Kind { get; set; }
    public string ActorSubject { get; set; } = string.Empty;
    public string? Note { get; set; }
    public string? CorrectionNote { get; set; }
    public long CreatedAtUtcTicks { get; set; }
}
