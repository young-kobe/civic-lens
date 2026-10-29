using CivicLens.Application.Collection.Jobs;

namespace CivicLens.Tests.Application.Collection.Jobs;

public sealed class CollectionLanePolicyTests
{
    [Fact]
    public void TheDefaultIsOneLaneSoBehaviorStaysTheSameUntilAnOperatorSetsMore()
    {
        Assert.Equal(1, CollectionLanePolicy.DefaultConcurrency);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public void ConcurrencyAcceptsInclusiveBounds(int concurrency)
    {
        Assert.Equal(concurrency, CollectionLanePolicy.ValidateConcurrency(concurrency));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(-1)]
    public void ConcurrencyRejectsValuesOutsideBounds(int concurrency)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CollectionLanePolicy.ValidateConcurrency(concurrency));
    }
}
