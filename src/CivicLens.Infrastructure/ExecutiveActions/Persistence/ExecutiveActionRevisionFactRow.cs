namespace CivicLens.Infrastructure.ExecutiveActions.Persistence;

internal sealed class ExecutiveActionRevisionFactRow
{
    public string RecordId { get; set; } = string.Empty;
    public int RevisionNumber { get; set; }
    public short Position { get; set; }
    public string CitationExtractionId { get; set; } = string.Empty;
    public int CitationStart { get; set; }
    public int CitationLength { get; set; }
}
