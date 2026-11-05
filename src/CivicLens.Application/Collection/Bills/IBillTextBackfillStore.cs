namespace CivicLens.Application.Collection.Bills;

/// <summary>The newest version of a bill that has no summary and no text read, and the source that gated it.</summary>
public sealed record BillTextBackfillTarget(string DetailJobId, string SourceId, string BillId);

public enum BillTextSkipReason { NoGate, HasSummary, HasTextState, NotAdmitted }

public sealed record SkippedBill(string BillId, BillTextSkipReason Reason);

/// <summary>The newest bills without text, and the listed bills that have none of them.</summary>
public sealed record BillTextListing(IReadOnlyList<BillTextBackfillTarget> Targets, IReadOnlyList<SkippedBill> Skipped);

public interface IBillTextBackfillStore
{
    Task<IReadOnlyList<BillTextBackfillTarget>> ListNewestBillsWithoutTextAsync(int limit, CancellationToken cancellationToken);

    Task<BillTextListing> ListNewestBillsWithoutTextAsync(IReadOnlyList<CongressBillListItem> bills, CancellationToken cancellationToken);

    /// <summary>Returns false when the gate has a summary, a text state or a newer gate of the same bill, or when the versions job exists.</summary>
    Task<bool> AdmitTextVersionsChainAsync(ConfiguredCollectionSource source, string detailJobId, CancellationToken cancellationToken);
}
