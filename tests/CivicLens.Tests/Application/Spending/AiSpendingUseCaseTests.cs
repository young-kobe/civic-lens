using CivicLens.Application.Spending;
using CivicLens.Core.Analysis;
using CivicLens.Core.Review;

namespace CivicLens.Tests.Application.Spending;

public sealed class AiSpendingUseCaseTests
{
    private const string Model = "claude-haiku-5-5";
    private static readonly ReviewActor Owner = new("auth0|owner", ReviewRole.Owner);
    private static readonly ReviewActor Reviewer = new("auth0|friend", ReviewRole.Reviewer);
    private readonly RecordingStore store = new();

    [Fact]
    public async Task AReviewerCannotReadOrChangeTheSpendingSettingsBecauseOnlyTheOwnerSpendsTheMoney()
    {
        var price = new SaveModelPriceRequest(Model, 1, 5, 0.1m, 1.25m);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new GetAiSpending(store).ExecuteAsync(Reviewer));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new SaveMonthlyCap(store).ExecuteAsync(Reviewer, 10));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new SaveModelPrice(store).ExecuteAsync(Reviewer, price));
        Assert.Empty(store.Saved);
    }

    [Fact]
    public async Task TheCapIsStoredInMicroDollarsUnderTheSubjectOfTheOwner()
    {
        await new SaveMonthlyCap(store).ExecuteAsync(Owner, 12.5m);

        Assert.Equal("cap auth0|owner 12500000", Assert.Single(store.Saved));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1_000_001)]
    [InlineData(0.0000001)]
    public async Task ACapOutsideTheRangeOrWithTooManyDecimalsIsRefusedBeforeItIsSaved(double dollars)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => new SaveMonthlyCap(store).ExecuteAsync(Owner, (decimal)dollars));

        Assert.Empty(store.Saved);
    }

    [Fact]
    public async Task AZeroCapIsAllowedSoTheOwnerCanStopAllAiCalls()
    {
        await new SaveMonthlyCap(store).ExecuteAsync(Owner, 0);

        Assert.Equal("cap auth0|owner 0", Assert.Single(store.Saved));
    }

    [Fact]
    public async Task APriceIsStoredInMicroDollarsForAModelThatAStageUses()
    {
        await new SaveModelPrice(store).ExecuteAsync(Owner, new SaveModelPriceRequest(Model, 1, 5, 0.1m, 1.25m));

        Assert.Equal($"price auth0|owner {Model} 1000000 5000000 100000 1250000", Assert.Single(store.Saved));
    }

    [Theory]
    [InlineData("claude-unused", 1)]
    [InlineData(Model, -1)]
    [InlineData(Model, 1_001)]
    public async Task AModelNoStageUsesOrAPriceOutsideTheRangeIsRefusedBeforeItIsSaved(string model, double dollars)
    {
        var request = new SaveModelPriceRequest(model, (decimal)dollars, 5, 0.1m, 1.25m);

        await Assert.ThrowsAsync<ArgumentException>(() => new SaveModelPrice(store).ExecuteAsync(Owner, request));

        Assert.Empty(store.Saved);
    }

    private sealed class RecordingStore : IAiSpendingStore
    {
        public List<string> Saved { get; } = [];

        public Task<AiSpendingView> GetAsync(int changeLimit, CancellationToken cancellationToken) => throw new InvalidOperationException();

        public Task<ModelPrice?> GetPriceAsync(string model, CancellationToken cancellationToken) => throw new InvalidOperationException();

        public Task AddChargeAsync(string model, AnalysisTokenUsage usage, long chargedTokens, CancellationToken cancellationToken) => throw new InvalidOperationException();

        public Task SaveCapAsync(string authorSubject, long capMicros, CancellationToken cancellationToken)
        {
            Saved.Add($"cap {authorSubject} {capMicros}");
            return Task.CompletedTask;
        }

        public Task SavePriceAsync(string authorSubject, ModelPrice price, CancellationToken cancellationToken)
        {
            Saved.Add($"price {authorSubject} {price.Model} {price.InputMicros} {price.OutputMicros} {price.CacheReadMicros} {price.CacheWriteMicros}");
            return Task.CompletedTask;
        }
    }
}
