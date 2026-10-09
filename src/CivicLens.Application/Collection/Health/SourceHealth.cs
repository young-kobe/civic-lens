namespace CivicLens.Application.Collection.Health;

/// <summary>
/// Latest check of a configured source. Failed with a RetryAt is an automatic retry and needs no action;
/// Checking with a RetryAt is evidence preparation that will resume by itself.
/// </summary>
public enum SourceCheckState { NeverChecked, Checking, Failed, Cancelled, ChangeFound, BaselineSaved, UpToDate }

/// <summary>ComparisonId is set only for ChangeFound. NeedsAttention is true only for terminal failures.</summary>
public sealed record SourceHealth(string SourceId, SourceCheckState State, DateTimeOffset? LastCheckedAt,
    DateTimeOffset? RetryAt, string? ComparisonId, bool NeedsAttention);

public sealed record SourceHealthReport(IReadOnlyList<SourceHealth> Sources, int AttentionCount);
