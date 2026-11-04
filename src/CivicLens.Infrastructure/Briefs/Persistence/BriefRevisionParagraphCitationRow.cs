namespace CivicLens.Infrastructure.Briefs.Persistence;

/// <summary>The citation ID that a paragraph names. Foreign keys prove that the paragraph and the listed citation exist in the same part of the same revision.</summary>
internal sealed class BriefRevisionParagraphCitationRow
{
    public string RecordId { get; set; } = string.Empty;
    public int RevisionNumber { get; set; }
    public BriefRevisionPart Part { get; set; }
    public short ParagraphPosition { get; set; }
    public short Position { get; set; }
    public string CitationId { get; set; } = string.Empty;
}
