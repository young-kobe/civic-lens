namespace CivicLens.Infrastructure.Votes.Persistence;

internal sealed class VoteRevisionFactRow
{
    public string RecordId { get; set; } = string.Empty;
    public int RevisionNumber { get; set; }
    public short Position { get; set; }
    public string FactText { get; set; } = string.Empty;
    public string CitationExtractionId { get; set; } = string.Empty;
    public int CitationStart { get; set; }
    public int CitationLength { get; set; }
}
