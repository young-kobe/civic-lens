namespace CivicLens.Application.Documents;

public sealed record DocumentComparisonBackfillResult(int Examined, int Moved, int Skipped,
    IReadOnlyList<string> SkippedComparisonIds);
