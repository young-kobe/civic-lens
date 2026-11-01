namespace CivicLens.Application.Collection.Bills;

public interface IBillStatusBackfillStore
{
    Task<IReadOnlyList<string>> ListNewestBillsWithoutStatusAsync(int limit, CancellationToken cancellationToken);

    Task<bool> RecordStatusAsync(string detailJobId, CongressBillLatestAction? latestAction, CancellationToken cancellationToken);
}
