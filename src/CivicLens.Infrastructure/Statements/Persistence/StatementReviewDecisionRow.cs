using CivicLens.Core.Review;
using CivicLens.Infrastructure.Review.Persistence;

namespace CivicLens.Infrastructure.Statements.Persistence;

internal sealed class StatementReviewDecisionRow : IReviewDecisionRow
{
    public string DecisionId { get; set; } = string.Empty;
    public string CandidateId { get; set; } = string.Empty;
    public int RevisionNumber { get; set; }
    public int ReviewStateVersion { get; set; }
    public string DecisionJson { get; set; } = string.Empty;
    public ReviewDecisionKind Kind { get; set; }
    public long CreatedAtUtcTicks { get; set; }

    string IReviewDecisionRow.RecordId => CandidateId;
}
