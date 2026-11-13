using CivicLens.PublicApi.ActiveRelease;

namespace CivicLens.Tests.PublicApi.ActiveRelease;

public sealed class ReleaseFreshnessTests
{
    private static readonly ReleaseFreshness Active = new(4, new(2026, 5, 31), new(2026, 5, 29));

    [Fact]
    public void ThePageOfTheActiveReleaseKeepsBothDays()
    {
        Assert.Equal(new(new(2026, 5, 31), new(2026, 5, 29)), Active.PageContextFor(4));
    }

    [Theory]
    [InlineData(3)]
    [InlineData(5)]
    public void ThePageOfAnotherReleaseGetsNoDaysBecauseTheyBelongToTheActiveRelease(int pageRelease)
    {
        Assert.Equal(new(null, null), Active.PageContextFor(pageRelease));
    }
}
