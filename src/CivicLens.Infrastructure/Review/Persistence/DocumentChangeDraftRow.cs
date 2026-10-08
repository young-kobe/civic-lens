namespace CivicLens.Infrastructure.Review.Persistence;

internal sealed class DocumentChangeDraftRow
{
    public string DraftId { get; set; } = string.Empty;
    public string ComparisonId { get; set; } = string.Empty;
    public int CurrentRevisionNumber { get; set; }
    public int ReviewStateVersion { get; set; }
    public long CreatedAtUtcTicks { get; set; }
}
