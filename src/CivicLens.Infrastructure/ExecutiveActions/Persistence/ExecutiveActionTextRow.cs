namespace CivicLens.Infrastructure.ExecutiveActions.Persistence;

/// <summary>The body text of one original document: its extraction and how many characters the extraction keeps.
/// The row has no text. The extraction holds it.</summary>
internal sealed class ExecutiveActionTextRow
{
    public string SourceId { get; set; } = string.Empty;
    public string DocumentNumber { get; set; } = string.Empty;
    public string ExtractionId { get; set; } = string.Empty;
    public int CharsTotal { get; set; }
    public int CharsRead { get; set; }
}
