using CivicLens.Core.Review;

namespace CivicLens.Infrastructure.Review.Persistence;

internal sealed class ReviewDecisionRow
{
    public string DecisionId { get; set; } = string.Empty;
    public string DraftId { get; set; } = string.Empty;
    public int RevisionNumber { get; set; }
    public int ReviewStateVersion { get; set; }
    public string DecisionJson { get; set; } = string.Empty;
    public ReviewDecisionKind Kind { get; set; }
    public long CreatedAtUtcTicks { get; set; }
}
