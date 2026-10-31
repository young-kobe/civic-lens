using CivicLens.Core.Review;
using CivicLens.Infrastructure.Review.Persistence;

namespace CivicLens.Infrastructure.Statements.Persistence;

internal sealed class StatementDraftRow : IListedDraftRow, IHeldDraftRow
{
    public string CandidateId { get; set; } = string.Empty;
    public int CurrentRevisionNumber { get; set; }
    public int ReviewStateVersion { get; set; }
    public ReviewStatus ReviewStatus { get; set; }
    public long CreatedAtUtcTicks { get; set; }
    public long LastRevisedAtUtcTicks { get; set; }
    public DateOnly EventOn { get; set; }
    public string[] IssueIds { get; set; } = [];
    public string[] OfficialIds { get; set; } = [];
    public string? HeldReason { get; set; }

    string IReviewDraftRow.RecordId => CandidateId;
}
