namespace CivicLens.Infrastructure.Review.Persistence;

internal sealed class DocumentChangeRevisionRow : IReviewRevisionRow
{
    public string DraftId { get; set; } = string.Empty;
    public int RevisionNumber { get; set; }
    public string RevisionJson { get; set; } = string.Empty;
    public long CreatedAtUtcTicks { get; set; }

    string IReviewRevisionRow.RecordId => DraftId;
}
