namespace CivicLens.Infrastructure.Actions.Persistence;

internal sealed class ActionRevisionKeyTermRow
{
    public string RecordId { get; set; } = string.Empty;
    public int RevisionNumber { get; set; }
    public short Position { get; set; }
    public string Term { get; set; } = string.Empty;
    public string CitationExtractionId { get; set; } = string.Empty;
    public int CitationStart { get; set; }
    public int CitationLength { get; set; }

    /// <summary>The stored characters of the cited span, kept here so the public view can serve the definition. Approval checks that they equal the span.</summary>
    public string Definition { get; set; } = string.Empty;
}
