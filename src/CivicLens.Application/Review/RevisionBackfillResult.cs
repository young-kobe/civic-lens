namespace CivicLens.Application.Review;

public sealed record RevisionBackfillResult(int Examined, int Moved, int Skipped,
    IReadOnlyDictionary<string, int> SkippedByReason, IReadOnlyList<SkippedRevision> SkippedRevisions);
