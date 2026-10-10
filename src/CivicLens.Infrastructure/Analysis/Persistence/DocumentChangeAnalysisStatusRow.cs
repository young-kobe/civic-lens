namespace CivicLens.Infrastructure.Analysis.Persistence;

internal sealed class DocumentChangeAnalysisStatusRow
{
    public string Task { get; set; } = string.Empty;
    public long? DailyTokenLimit { get; set; }
    public long RecordedAtUtcTicks { get; set; }
    public string? PausedReason { get; set; }
    public long? PausedUntilUtcTicks { get; set; }
}
