namespace CivicLens.Infrastructure.Briefs.Persistence;

internal sealed class BriefRevisionParagraphRow
{
    public string RecordId { get; set; } = string.Empty;
    public int RevisionNumber { get; set; }
    public BriefRevisionPart Part { get; set; }
    public short Position { get; set; }
    public string ParagraphText { get; set; } = string.Empty;
    public List<BriefRevisionParagraphCitationRow> Citations { get; set; } = [];
}
