using CivicLens.PublicApi;

namespace CivicLens.Tests.PublicApi;

public sealed class PublicApiOptionsTests
{
    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("many")]
    public void ARateLimitBelowOneOrNotANumberIsRefusedAtStartupBecauseZeroWouldBlockEveryReader(string value)
    {
        Assert.Throws<ArgumentException>(() => PublicApiOptions.ParseRequestsPerMinute(value));
    }

    [Fact]
    public void AMissingRateLimitMeansSixtyAndAValidOneIsKept()
    {
        Assert.Equal(60, PublicApiOptions.ParseRequestsPerMinute(null));
        Assert.Equal(7, PublicApiOptions.ParseRequestsPerMinute("7"));
    }
}
