using CivicLens.Application.Collection.Processing;

namespace CivicLens.Tests.Application.Collection.Processing;

public sealed class EvidenceProcessingPolicyTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(600)]
    public void LeaseDurationAcceptsInclusiveBounds(int seconds)
    {
        EvidenceProcessingPolicy.ValidateLeaseDuration(TimeSpan.FromSeconds(seconds));
    }

    [Theory]
    [InlineData(0.999)]
    [InlineData(600.001)]
    public void LeaseDurationRejectsValuesOutsideBounds(double seconds)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            EvidenceProcessingPolicy.ValidateLeaseDuration(TimeSpan.FromSeconds(seconds)));
    }
}
