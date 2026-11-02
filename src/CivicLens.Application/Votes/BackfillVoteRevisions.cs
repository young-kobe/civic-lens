using CivicLens.Application.Review;
using CivicLens.Core.Review;

namespace CivicLens.Application.Votes;

public sealed record VoteRevisionBackfillResult(int Examined, int Moved, int Skipped,
    IReadOnlyDictionary<string, int> SkippedByReason, IReadOnlyList<SkippedVoteRevision> SkippedRevisions);

public sealed class BackfillVoteRevisions(IVoteRevisionBackfillStore store)
{
    private const int BatchSize = 25;
    private const int ReportedSkips = 50;

    public async Task<VoteRevisionBackfillResult> ExecuteAsync(ReviewActor actor, int limit, CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireOwner(actor);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        var examined = 0;
        var skipped = new List<SkippedVoteRevision>();
        VoteRevisionKey? after = null;
        while (examined < limit)
        {
            var batch = await store.BackfillBatchAsync(after, Math.Min(BatchSize, limit - examined), cancellationToken);
            if (batch.Last is null) break;
            examined += batch.Examined;
            skipped.AddRange(batch.Skipped);
            after = batch.Last;
        }
        var byReason = skipped.GroupBy(revision => revision.Reason).ToDictionary(group => group.Key, group => group.Count());
        return new(examined, examined - skipped.Count, skipped.Count, byReason, [.. skipped.Take(ReportedSkips)]);
    }
}
