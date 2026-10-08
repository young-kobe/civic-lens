namespace CivicLens.Host.Pages.Review;

public sealed record SourceProcessingSummary(
    string Label,
    string? Detail,
    string? ComparisonId,
    bool NeedsAttention,
    bool IsActive);
