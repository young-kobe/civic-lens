namespace CivicLens.Infrastructure.Publication.Persistence;

internal sealed class PublicationWithdrawalCorrectionRow
{
    public string Kind { get; set; } = string.Empty;
    public string RecordId { get; set; } = string.Empty;
    public int FirstRelease { get; set; }
    public int Position { get; set; }
    public int ReplacedRevisionNumber { get; set; }
    public long PublishedAtUtcTicks { get; set; }
    public string? Note { get; set; }
    public bool FactsUnchanged { get; set; }
}
