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

    /// <summary>The plain form of the cited span: its stored characters with each GPO quote mark pair made one plain double quote. It is kept here so the
    /// public view can serve the definition. Approval checks that it equals the plain form of the span.</summary>
    public string Definition { get; set; } = string.Empty;

    /// <summary>True when the quote ends before the end of its definition paragraph. Code decides it at draft time, and approval checks it again.</summary>
    public bool Cut { get; set; }
}
