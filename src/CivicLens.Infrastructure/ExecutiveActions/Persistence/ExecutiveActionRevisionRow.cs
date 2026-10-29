using CivicLens.Infrastructure.Review.Persistence;

namespace CivicLens.Infrastructure.ExecutiveActions.Persistence;

internal sealed class ExecutiveActionRevisionRow : IReviewRevisionRow
{
    public string RecordId { get; set; } = string.Empty;
    public int RevisionNumber { get; set; }
    public string RevisionJson { get; set; } = string.Empty;
    public long CreatedAtUtcTicks { get; set; }
}
