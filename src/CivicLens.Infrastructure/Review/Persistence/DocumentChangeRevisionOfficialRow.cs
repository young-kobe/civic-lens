namespace CivicLens.Infrastructure.Review.Persistence;

internal sealed class DocumentChangeRevisionOfficialRow
{
    public string DraftId { get; set; } = string.Empty;
    public int RevisionNumber { get; set; }
    public string OfficialId { get; set; } = string.Empty;
    public short Position { get; set; }
}
