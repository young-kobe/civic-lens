using CivicLens.Application.Review;
using CivicLens.Core.Review;

namespace CivicLens.Application.Documents;

public sealed class BackfillDocumentComparisonDetails(IDocumentComparisonBackfillStore store)
{
    private const int BatchSize = 25;
    private const int ReportedSkips = 50;

    public async Task<DocumentComparisonBackfillResult> ExecuteAsync(ReviewActor actor, int limit,
        CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireOwner(actor);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        var examined = 0;
        var moved = 0;
        var skipped = new List<string>();
        string? after = null;
        while (examined < limit)
        {
            var batch = await store.BackfillBatchAsync(after, Math.Min(BatchSize, limit - examined), cancellationToken);
            if (batch.LastComparisonId is null) break;
            examined += batch.Examined;
            moved += batch.Examined - batch.Skipped.Length;
            skipped.AddRange(batch.Skipped.Take(Math.Max(0, ReportedSkips - skipped.Count)));
            after = batch.LastComparisonId;
        }
        return new(examined, moved, examined - moved, skipped);
    }
}
