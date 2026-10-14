using CivicLens.Core.Review;
using CivicLens.Infrastructure.Review.Persistence;

namespace CivicLens.Infrastructure.Statements.Persistence;

internal sealed class StatementDraftRow : IReviewDraftRow
{
    public string CandidateId { get; set; } = string.Empty;
    public int CurrentRevisionNumber { get; set; }
    public int ReviewStateVersion { get; set; }
    public ReviewStatus ReviewStatus { get; set; }
    public long CreatedAtUtcTicks { get; set; }

    string IReviewDraftRow.RecordId => CandidateId;
}
