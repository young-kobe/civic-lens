using CivicLens.Core.Analysis;

namespace CivicLens.Application.Spending;

public interface IAiSpendingStore
{
    Task<AiSpendingView> GetAsync(int changeLimit, CancellationToken cancellationToken);

    /// <summary>Adds the cost of finished calls to the month at the price of the model. Each usage in the list picks the price tier for its own tokens. A usage can sum several passes over one bill. Tokens charged beyond the usage count at the highest price.</summary>
    Task AddChargeAsync(string model, IReadOnlyCollection<AnalysisTokenUsage> calls, long chargedTokens, CancellationToken cancellationToken);
    Task SaveCapAsync(string authorSubject, long capMicros, CancellationToken cancellationToken);
}
