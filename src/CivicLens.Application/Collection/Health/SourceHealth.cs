namespace CivicLens.Application.Collection.Health;

public enum SourceCheckState { NeverChecked, Checking, Failed, Cancelled, ChangeFound, BaselineSaved, UpToDate }

public sealed record SourceHealth(string SourceId, SourceCheckState State, DateTimeOffset? LastCheckedAt,
    DateTimeOffset? RetryAt, string? ComparisonId, bool NeedsAttention, string? JobId = null);

public sealed record SourceHealthReport(IReadOnlyList<SourceHealth> Sources, int AttentionCount);
