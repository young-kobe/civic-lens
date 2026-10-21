using CivicLens.Core.Review;
using CivicLens.Core.Votes;
using CivicLens.Infrastructure.Review.Persistence;

namespace CivicLens.Infrastructure.Votes.Persistence;

internal sealed class VoteDraftRow : IListedDraftRow
{
    public string RecordId { get; set; } = string.Empty;
    public string SourceId { get; set; } = string.Empty;
    public Chamber Chamber { get; set; }
    public int Congress { get; set; }
    public int SessionNumber { get; set; }
    public int RollCallNumber { get; set; }
    public string CandidateId { get; set; } = string.Empty;
    public int CurrentRevisionNumber { get; set; }
    public int ReviewStateVersion { get; set; }
    public ReviewStatus ReviewStatus { get; set; }
    public long CreatedAtUtcTicks { get; set; }
    public DateOnly EventOn { get; set; }
    public string[] IssueIds { get; set; } = [];
    public string[] OfficialIds { get; set; } = [];
}
