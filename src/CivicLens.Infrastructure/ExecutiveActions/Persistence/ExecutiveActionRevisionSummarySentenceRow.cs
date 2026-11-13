namespace CivicLens.Infrastructure.ExecutiveActions.Persistence;

/// <summary>One sentence of the summary of a revision, with the span of its quote in the body text extraction.</summary>
internal sealed class ExecutiveActionRevisionSummarySentenceRow
{
    public string RecordId { get; set; } = string.Empty;
    public int RevisionNumber { get; set; }
    public short Position { get; set; }
    public string Sentence { get; set; } = string.Empty;
    public string QuoteExtractionId { get; set; } = string.Empty;
    public int QuoteStart { get; set; }
    public int QuoteLength { get; set; }
}
