namespace CivicLens.Application.Collection.Bills;

/// <summary>The newest version of a bill that has no summary and no text read, and the source that gated it.</summary>
public sealed record BillTextBackfillTarget(string DetailJobId, string SourceId);

public interface IBillTextBackfillStore
{
    Task<int> CountPendingActionDraftsAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<BillTextBackfillTarget>> ListNewestBillsWithoutTextAsync(int limit, CancellationToken cancellationToken);

    Task<IReadOnlyList<BillTextBackfillTarget>> ListNewestBillsWithoutTextAsync(IReadOnlyList<CongressBillListItem> bills,
        CancellationToken cancellationToken);

    Task<bool> AdmitTextVersionsChainAsync(ConfiguredCollectionSource source, string detailJobId, CancellationToken cancellationToken);
}
