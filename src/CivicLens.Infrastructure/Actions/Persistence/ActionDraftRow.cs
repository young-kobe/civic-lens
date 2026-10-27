using CivicLens.Core.Review;
using CivicLens.Infrastructure.Review.Persistence;

namespace CivicLens.Infrastructure.Actions.Persistence;

internal sealed class ActionDraftRow : IListedDraftRow
{
    public string RecordId { get; set; } = string.Empty;
    public string SourceId { get; set; } = string.Empty;
    public int Congress { get; set; }
    public string BillType { get; set; } = string.Empty;
    public int BillNumber { get; set; }
    public string CandidateId { get; set; } = string.Empty;
    public int CurrentRevisionNumber { get; set; }
    public int ReviewStateVersion { get; set; }
    public ReviewStatus ReviewStatus { get; set; }
    public long CreatedAtUtcTicks { get; set; }
    public long LastRevisedAtUtcTicks { get; set; }
    public DateOnly EventOn { get; set; }
    public string[] IssueIds { get; set; } = [];
    public string[] OfficialIds { get; set; } = [];
}
