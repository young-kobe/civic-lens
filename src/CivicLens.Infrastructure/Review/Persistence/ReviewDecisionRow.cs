using CivicLens.Core.Review;

namespace CivicLens.Infrastructure.Review.Persistence;

internal sealed class ReviewDecisionRow : IReviewDecisionRow
{
    public string DecisionId { get; set; } = string.Empty;
    public string DraftId { get; set; } = string.Empty;
    public int RevisionNumber { get; set; }
    public int ReviewStateVersion { get; set; }
    public ReviewDecisionKind Kind { get; set; }
    public string ActorSubject { get; set; } = string.Empty;
    public string? Note { get; set; }
    public string? CorrectionNote { get; set; }
    public long CreatedAtUtcTicks { get; set; }

    string IReviewDecisionRow.RecordId
    {
        get => DraftId;
        set => DraftId = value;
    }
}
