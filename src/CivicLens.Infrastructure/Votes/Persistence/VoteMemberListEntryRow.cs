namespace CivicLens.Infrastructure.Votes.Persistence;

internal sealed class VoteMemberListEntryRow
{
    public string ListId { get; set; } = string.Empty;
    public short Position { get; set; }
    public string PersonId { get; set; } = string.Empty;
    public string BioguideId { get; set; } = string.Empty;
    public string VoteCast { get; set; } = string.Empty;
    public string CitationExtractionId { get; set; } = string.Empty;
    public int CitationStart { get; set; }
    public int CitationLength { get; set; }
}
