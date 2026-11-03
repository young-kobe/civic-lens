using CivicLens.Core.Review;

namespace CivicLens.Application.Review;

public sealed record RevisionBackfillResult(int Examined, int Moved, int Skipped,
    IReadOnlyDictionary<string, int> SkippedByReason, IReadOnlyList<SkippedRevision> SkippedRevisions);

public sealed class BackfillRevisions(IRevisionBackfillStore store)
{
    private const int BatchSize = 25;
    private const int ReportedSkips = 50;

    public async Task<RevisionBackfillResult> ExecuteAsync(ReviewActor actor, int limit, CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireOwner(actor);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        int examined = 0, moved = 0;
        var skipped = new List<SkippedRevision>();
        RevisionKey? after = null;
        while (moved < limit)
        {
            var batch = await store.BackfillBatchAsync(after, Math.Min(BatchSize, limit - moved), cancellationToken);
            if (batch.Last is null) break;
            examined += batch.Examined;
            moved += batch.Examined - batch.Skipped.Length;
            skipped.AddRange(batch.Skipped);
            after = batch.Last;
        }
        var byReason = skipped.GroupBy(revision => revision.Reason).ToDictionary(group => group.Key, group => group.Count());
        return new(examined, moved, skipped.Count, byReason, [.. skipped.Take(ReportedSkips)]);
    }
}
