using CivicLens.Application.Spending;

namespace CivicLens.Tests.Fixtures;

internal sealed class FakeAiSpendingStore(ModelPrice? price) : IAiSpendingStore
{
    public List<long> Charges { get; } = [];

    public Task<ModelPrice?> GetPriceAsync(string model, CancellationToken cancellationToken) => Task.FromResult(price);

    public Task AddChargeAsync(long micros, CancellationToken cancellationToken)
    {
        Charges.Add(micros);
        return Task.CompletedTask;
    }

    public Task<AiSpendingView> GetAsync(int changeLimit, CancellationToken cancellationToken) => throw new InvalidOperationException();

    public Task SaveCapAsync(string authorSubject, long capMicros, CancellationToken cancellationToken) => throw new InvalidOperationException();

    public Task SavePriceAsync(string authorSubject, ModelPrice price, CancellationToken cancellationToken) => throw new InvalidOperationException();
}
