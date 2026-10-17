using CivicLens.Core.Review;
using CivicLens.Infrastructure.Review.Persistence;

namespace CivicLens.Infrastructure.Relationships.Persistence;

internal sealed class RelationshipDraftRow : IReviewDraftRow
{
    public string RecordId { get; set; } = string.Empty;
    public int CurrentRevisionNumber { get; set; }
    public int ReviewStateVersion { get; set; }
    public ReviewStatus ReviewStatus { get; set; }
    public bool NoLongerMatches { get; set; }
    public long CreatedAtUtcTicks { get; set; }
    public long LastRevisedAtUtcTicks { get; set; }
}
