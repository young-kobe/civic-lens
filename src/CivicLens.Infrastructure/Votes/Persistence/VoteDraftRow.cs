using CivicLens.Core.Review;
using CivicLens.Infrastructure.Review.Persistence;

namespace CivicLens.Infrastructure.Votes.Persistence;

internal sealed class VoteDraftRow : IReviewDraftRow
{
    public string RecordId { get; set; } = string.Empty;
    public string SourceId { get; set; } = string.Empty;
    public int Congress { get; set; }
    public int SessionNumber { get; set; }
    public int RollCallNumber { get; set; }
    public int CurrentRevisionNumber { get; set; }
    public int ReviewStateVersion { get; set; }
    public ReviewStatus ReviewStatus { get; set; }
    public long CreatedAtUtcTicks { get; set; }
    public long LastRevisedAtUtcTicks { get; set; }
}
