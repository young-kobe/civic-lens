namespace CivicLens.Application.Collection.Bills;

public interface IBillActionsBackfillStore
{
    Task<IReadOnlyList<string>> ListBillsWithUnverifiedActionsAsync(string? after, int limit, CancellationToken cancellationToken);

    Task<IReadOnlyList<BillActionsBackfillPage>> ListActionPagesAsync(string detailJobId, CancellationToken cancellationToken);

    Task<bool> RecordVerifiedActionChainAsync(string detailJobId, IReadOnlyList<VerifiedBillActionsPage> pages,
        CancellationToken cancellationToken);
}
