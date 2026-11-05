namespace CivicLens.Application.Spending;

public interface IAiSpendingStore
{
    Task<AiSpendingView> GetAsync(int changeLimit, CancellationToken cancellationToken);
    Task SaveCapAsync(string authorSubject, long capMicros, CancellationToken cancellationToken);
    Task SavePriceAsync(string authorSubject, ModelPrice price, CancellationToken cancellationToken);
}
