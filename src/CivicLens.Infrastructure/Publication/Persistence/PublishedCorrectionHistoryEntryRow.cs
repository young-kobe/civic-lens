namespace CivicLens.Infrastructure.Publication.Persistence;

internal sealed class PublishedCorrectionHistoryEntryRow
{
    public string HistoryId { get; set; } = string.Empty;
    public short Position { get; set; }
    public int ReplacedRevisionNumber { get; set; }
    public long PublishedAtUtcTicks { get; set; }
    public short PublishedOffsetMinutes { get; set; }
    public string? Note { get; set; }
    public bool FactsUnchanged { get; set; }
}
