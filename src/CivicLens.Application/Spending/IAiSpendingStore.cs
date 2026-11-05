using CivicLens.Core.Analysis;

namespace CivicLens.Application.Spending;

public interface IAiSpendingStore
{
    Task<AiSpendingView> GetAsync(int changeLimit, CancellationToken cancellationToken);
    Task<ModelPrice?> GetPriceAsync(string model, CancellationToken cancellationToken);

    /// <summary>Adds the cost of finished calls to the month at the newest price of the model. Tokens charged beyond the usage count at the highest price.</summary>
    Task AddChargeAsync(string model, AnalysisTokenUsage usage, long chargedTokens, CancellationToken cancellationToken);
    Task SaveCapAsync(string authorSubject, long capMicros, CancellationToken cancellationToken);
    Task SavePriceAsync(string authorSubject, ModelPrice price, CancellationToken cancellationToken);
}
