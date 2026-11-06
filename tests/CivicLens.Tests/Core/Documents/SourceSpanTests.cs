using CivicLens.Core.Documents;

namespace CivicLens.Tests.Core.Documents;

public sealed class SourceSpanTests
{
    [Fact]
    public void ACitationEndThatOverflowsInt32CannotAppearInsideAnotherSpan()
    {
        var outer = new SourceSpan(new string('a', 64), int.MaxValue - 1, 1);
        var overflowing = outer with { Length = 2 };

        Assert.False(outer.Covers(overflowing));
        Assert.True(overflowing.Covers(outer));
    }
}
