using CivicLens.Core.Review;
using CivicLens.Infrastructure.Review.Persistence;

namespace CivicLens.Infrastructure.Briefs.Persistence;

internal sealed class BriefDraftRow : IReviewDraftRow
{
    public string RecordId { get; set; } = string.Empty;
    public DateOnly WeekStart { get; set; }
    public int CurrentRevisionNumber { get; set; }
    public int ReviewStateVersion { get; set; }
    public ReviewStatus ReviewStatus { get; set; }
    public long CreatedAtUtcTicks { get; set; }
    public long LastRevisedAtUtcTicks { get; set; }
}
