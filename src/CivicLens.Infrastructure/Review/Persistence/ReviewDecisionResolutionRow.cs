namespace CivicLens.Infrastructure.Review.Persistence;

/// <summary>One concern that an approval resolved. The type argument picks the decision table and so the record kind.</summary>
internal sealed class ReviewDecisionResolutionRow<TDecisionRow> where TDecisionRow : class, IReviewDecisionRow
{
    public string DecisionId { get; set; } = string.Empty;
    public string RecordId { get; set; } = string.Empty;
    public string ResolvedDecisionId { get; set; } = string.Empty;
}
