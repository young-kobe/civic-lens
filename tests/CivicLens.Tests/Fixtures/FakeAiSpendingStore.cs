using CivicLens.Application.Spending;
using CivicLens.Core.Analysis;

namespace CivicLens.Tests.Fixtures;

internal sealed class FakeAiSpendingStore(ModelPrice price) : IAiSpendingStore
{
    public List<long> Charges { get; } = [];
    public Exception? ChargeFailure { get; init; }

    public Task AddChargeAsync(string model, IReadOnlyCollection<AnalysisTokenUsage> calls, long chargedTokens, CancellationToken cancellationToken)
    {
        if (ChargeFailure is not null) throw ChargeFailure;
        Charges.Add(price.CostMicros(calls, chargedTokens));
        return Task.CompletedTask;
    }

    public Task<AiSpendingView> GetAsync(int changeLimit, CancellationToken cancellationToken) => throw new InvalidOperationException();

    public Task SaveCapAsync(string authorSubject, long capMicros, CancellationToken cancellationToken) => throw new InvalidOperationException();
}
