using CivicLens.Application.Review;
using CivicLens.Core.Review;

namespace CivicLens.Application.Collection.Bills;

/// <summary>PendingActionDrafts above 0 means the run was refused and nothing was examined.</summary>
public sealed record BillTextBackfillResult(int PendingActionDrafts, int Examined, int Admitted);

/// <summary>
/// Admits a bill text versions job for bills that were gated before the text source existed. The jobs
/// carry the revision of the current configuration, because the old detail jobs hold a revision with no enabled text source.
/// </summary>
public sealed class BackfillBillText(IBillTextBackfillStore store, CollectionConfigurationRevision revision)
{
    public async Task<BillTextBackfillResult> ExecuteAsync(ReviewActor actor, int limit, CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireOwner(actor);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        return await RunAsync(token => store.ListNewestBillsWithoutTextAsync(limit, token), cancellationToken);
    }

    public async Task<BillTextBackfillResult> ExecuteAsync(ReviewActor actor, IReadOnlyList<CongressBillListItem> bills,
        CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireOwner(actor);
        ArgumentOutOfRangeException.ThrowIfZero(bills.Count);
        return await RunAsync(token => store.ListNewestBillsWithoutTextAsync(bills, token), cancellationToken);
    }

    private async Task<BillTextBackfillResult> RunAsync(Func<CancellationToken, Task<IReadOnlyList<BillTextBackfillTarget>>> list,
        CancellationToken cancellationToken)
    {
        if (revision.Configuration.SourceOf(ApiParser.CongressBillText) is not { Enabled: true })
            throw new ArgumentException("The collection configuration has no enabled bill text source.");
        if (await store.CountPendingActionDraftsAsync(cancellationToken) is var pending and > 0) return new(pending, 0, 0);
        var targets = await list(cancellationToken);
        var admitted = 0;
        foreach (var target in targets)
            if (await store.AdmitTextVersionsChainAsync(new(revision, target.SourceId), target.DetailJobId, cancellationToken)) admitted++;
        return new(0, targets.Count, admitted);
    }
}
