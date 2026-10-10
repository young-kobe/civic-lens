namespace CivicLens.Application.Analysis;

public sealed record DocumentChangeAnalysisStatus(long? DailyTokenLimit, DateTimeOffset RecordedAtUtc, string? PausedReason = null,
    DateTimeOffset? PausedUntilUtc = null)
{
    public bool Enabled => DailyTokenLimit is not null;

    public bool IsPaused(DateTimeOffset now) => PausedReason is not null && (PausedUntilUtc is null || PausedUntilUtc > now);
}
