using CivicLens.Application.Spending;
using CivicLens.Core.Analysis;

namespace CivicLens.Tests.Application.Spending;

public sealed class ModelPriceTests
{
    private static readonly ModelPrice Haiku = ModelPrices.For("claude-haiku-5-5");
    private static readonly ModelPrice Sonnet = ModelPrices.For("claude-sonnet-5-5");

    [Fact]
    public void EveryModelThatAStageUsesHasAPriceBecauseTheCapNeedsTheCostOfEveryCall()
    {
        Assert.All(AiSpendingPolicy.ModelsInUse, model => Assert.Equal(model, ModelPrices.For(model).Model));
    }

    [Fact]
    public void EveryModelListsItsTiersFromSmallToLargeWithOnlyTheLastUnlimitedBecauseTheTierOfACallIsTheFirstThatFits()
    {
        Assert.All(AiSpendingPolicy.ModelsInUse, model =>
        {
            var limits = ModelPrices.For(model).Tiers.Select(tier => tier.PromptTokensUpTo).ToArray();
            Assert.Null(limits[^1]);
            Assert.Equal(limits[..^1].OrderBy(limit => limit), limits[..^1]);
            Assert.All(limits[..^1], limit => Assert.NotNull(limit));
        });
    }

    [Fact]
    public void AModelWithNoPriceIsAProgrammingErrorBecauseItsCostWouldBeUnknown()
    {
        Assert.Throws<InvalidOperationException>(() => ModelPrices.For("claude-unpriced"));
    }

    [Fact]
    public void TheWorstCaseUsesTheHighestPriceOfAnyTierBecauseAReservationCannotKnowTheSizeOfThePrompt()
    {
        Assert.Equal(12_500, Haiku.WorstCaseMicros(5_000));
        Assert.Equal(50_000, Sonnet.WorstCaseMicros(5_000));
    }

    [Fact]
    public void AMicroDollarThatIsPartlyUsedIsRoundedUpSoTheCapNeverHidesSpend()
    {
        var cheap = new ModelPrice("cheap", [new(null, 1, 1, 1, 1)]);

        Assert.Equal(1, cheap.WorstCaseMicros(1));
        Assert.Equal(0, cheap.WorstCaseMicros(0));
    }

    [Fact]
    public void TheCostPricesEachKindOfTokenAtItsOwnPrice()
    {
        var usage = new AnalysisTokenUsage(1_000, 500, 2_000, 100);

        Assert.Equal(383, Haiku.CostMicros([usage], usage.Total));
    }

    [Fact]
    public void ChargedTokensBeyondTheUsedTokensCostTheHighestPriceBecauseAFailedCallMayHaveBeenBilled()
    {
        var usage = new AnalysisTokenUsage(1_000, 0, 0, 0);

        Assert.Equal(110, Haiku.CostMicros([usage], 1_004));
    }

    [Fact]
    public void APromptOfExactlyTheTierLimitPaysTheLowerPrices()
    {
        Assert.Equal(10_000, Haiku.CostMicros([new AnalysisTokenUsage(100_000, 0, 0, 0)], 100_000));
    }

    [Fact]
    public void APromptOneTokenOverTheTierLimitPaysTheUpperPricesForTheWholeCall()
    {
        Assert.Equal(50_001 + 500, Haiku.CostMicros([new AnalysisTokenUsage(100_001, 200, 0, 0)], 100_201));
    }

    [Theory]
    [InlineData(40_001, 0, 32_001)]
    [InlineData(0, 40_001, 55_001)]
    public void CacheReadAndCacheWriteTokensCountTowardsThePromptSize(long cacheRead, long cacheWrite, long expectedMicros)
    {
        var usage = new AnalysisTokenUsage(60_000, 0, cacheRead, cacheWrite);

        Assert.Equal(expectedMicros, Haiku.CostMicros([usage], usage.Total));
    }

    [Fact]
    public void EachCallPicksItsOwnTierBecauseTwoSmallPromptsAreNotOneLargePrompt()
    {
        var call = new AnalysisTokenUsage(60_000, 0, 0, 0);

        Assert.Equal(12_000, Haiku.CostMicros([call, call], 120_000));
    }
}
