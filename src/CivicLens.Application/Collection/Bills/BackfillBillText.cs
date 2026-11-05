using CivicLens.Application.Actions;
using CivicLens.Application.Review;
using CivicLens.Core.Review;

namespace CivicLens.Application.Collection.Bills;

/// <summary>PendingActionDrafts above 0 means the run was refused and nothing was examined.</summary>
public sealed record BillTextBackfillResult(int PendingActionDrafts, int Examined, int Admitted, IReadOnlyList<SkippedBill> Skipped);

/// <summary>
/// Admits a bill text versions job for bills that were gated before the text source existed. The jobs
/// carry the revision of the current configuration, because the old detail jobs hold a revision with no enabled text source.
/// </summary>
public sealed class BackfillBillText(IBillTextBackfillStore store, IActionDraftStore drafts, CollectionConfigurationRevision revision)
{
    public async Task<BillTextBackfillResult> ExecuteAsync(ReviewActor actor, int limit, CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireOwner(actor);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        return await RunAsync(async token => new BillTextListing(await store.ListNewestBillsWithoutTextAsync(limit, token), []), cancellationToken);
    }

    public async Task<BillTextBackfillResult> ExecuteAsync(ReviewActor actor, IReadOnlyList<CongressBillListItem> bills,
        CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireOwner(actor);
        ArgumentOutOfRangeException.ThrowIfZero(bills.Count);
        return await RunAsync(token => store.ListNewestBillsWithoutTextAsync(bills, token), cancellationToken);
    }

    private async Task<BillTextBackfillResult> RunAsync(Func<CancellationToken, Task<BillTextListing>> list, CancellationToken cancellationToken)
    {
        if (revision.Configuration.SourceOf(ApiParser.CongressBillText) is not { Enabled: true })
            throw new ArgumentException("The collection configuration has no enabled bill text source.");
        if ((await drafts.CountAsync(cancellationToken)).GetValueOrDefault(ActionDraftView.Pending) is var pending and > 0) return new(pending, 0, 0, []);
        var listing = await list(cancellationToken);
        RequireKnownSources(listing.Targets);
        var admitted = 0;
        var skipped = listing.Skipped.ToList();
        foreach (var target in listing.Targets)
            if (await store.AdmitTextVersionsChainAsync(new(revision, target.SourceId), target.DetailJobId, cancellationToken)) admitted++;
            else skipped.Add(new(target.BillId, BillTextSkipReason.NotAdmitted));
        return new(0, listing.Targets.Count, admitted, skipped);
    }

    private void RequireKnownSources(IReadOnlyList<BillTextBackfillTarget> targets)
    {
        var unknown = targets.Select(target => target.SourceId).Distinct().FirstOrDefault(id => revision.Configuration.Sources.All(source => source.Id != id));
        if (unknown is not null) throw new ArgumentException($"The collection configuration has no source '{unknown}'.");
    }
}
