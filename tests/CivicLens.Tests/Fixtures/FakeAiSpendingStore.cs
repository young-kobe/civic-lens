using CivicLens.Application.Spending;
using CivicLens.Core.Analysis;

namespace CivicLens.Tests.Fixtures;

internal sealed class FakeAiSpendingStore(ModelPrice? price) : IAiSpendingStore
{
    public List<long> Charges { get; } = [];
    public Exception? ChargeFailure { get; init; }

    public Task<ModelPrice?> GetPriceAsync(string model, CancellationToken cancellationToken) => Task.FromResult(price);

    public Task AddChargeAsync(string model, AnalysisTokenUsage usage, long chargedTokens, CancellationToken cancellationToken)
    {
        if (ChargeFailure is not null) throw ChargeFailure;
        Charges.Add((price ?? throw new InvalidOperationException("A charge needs a price.")).CostMicros(usage, chargedTokens));
        return Task.CompletedTask;
    }

    public Task<AiSpendingView> GetAsync(int changeLimit, CancellationToken cancellationToken) => throw new InvalidOperationException();

    public Task SaveCapAsync(string authorSubject, long capMicros, CancellationToken cancellationToken) => throw new InvalidOperationException();

    public Task SavePriceAsync(string authorSubject, ModelPrice price, CancellationToken cancellationToken) => throw new InvalidOperationException();
}
