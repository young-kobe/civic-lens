using CivicLens.Application.Spending;
using CivicLens.Core.Analysis;

namespace CivicLens.Tests.Application.Spending;

public sealed class ModelPriceTests
{
    private static readonly ModelPrice Haiku = new("claude-haiku-5-5", 1_000_000, 5_000_000, 100_000, 1_250_000);

    [Fact]
    public void TheWorstCaseUsesTheHighestPriceBecauseAReservationCannotKnowTheSplitOfTheTokens()
    {
        Assert.Equal(25_000, Haiku.WorstCaseMicros(5_000));
    }

    [Fact]
    public void AMicroDollarThatIsPartlyUsedIsRoundedUpSoTheCapNeverHidesSpend()
    {
        var cheap = new ModelPrice("cheap", 1, 1, 1, 1);

        Assert.Equal(1, cheap.WorstCaseMicros(1));
        Assert.Equal(0, cheap.WorstCaseMicros(0));
    }

    [Fact]
    public void TheCostPricesEachKindOfTokenAtItsOwnPrice()
    {
        var usage = new AnalysisTokenUsage(1_000, 500, 2_000, 100);

        Assert.Equal(3_825, Haiku.CostMicros(usage, usage.Total));
    }

    [Fact]
    public void ChargedTokensBeyondTheUsedTokensCostTheHighestPriceBecauseAFailedCallMayHaveBeenBilled()
    {
        var usage = new AnalysisTokenUsage(1_000, 0, 0, 0);

        Assert.Equal(1_000 + 4 * 5, Haiku.CostMicros(usage, 1_004));
    }
}
