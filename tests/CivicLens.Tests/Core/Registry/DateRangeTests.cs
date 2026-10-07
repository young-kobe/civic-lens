using CivicLens.Core.Registry;

namespace CivicLens.Tests.Core.Registry;

public sealed class DateRangeTests
{
    [Fact]
    public void ContainsUsesInclusiveStartExclusiveEndAndUnspecifiedBounds()
    {
        var range = new DateRange(new DateOnly(2024, 1, 1), new DateOnly(2024, 2, 1));
        Assert.True(range.Contains(new DateOnly(2024, 1, 1)));
        Assert.True(range.Contains(new DateOnly(2024, 1, 31)));
        Assert.False(range.Contains(new DateOnly(2023, 12, 31)));
        Assert.False(range.Contains(new DateOnly(2024, 2, 1)));
        Assert.True(new DateRange(null, new DateOnly(2024, 1, 1)).Contains(new DateOnly(1900, 1, 1)));
        Assert.True(new DateRange(new DateOnly(2024, 1, 1), null).Contains(new DateOnly(9999, 12, 31)));
        Assert.True(new DateRange(null, null).Contains(new DateOnly(2024, 1, 1)));
    }

    [Fact]
    public void RejectsEmptyOrReversedBoundedRangesAndRecognizesAdjacency()
    {
        Assert.Throws<ArgumentException>(() => new DateRange(new DateOnly(2024, 2, 1), new DateOnly(2024, 2, 1)));
        Assert.Throws<ArgumentException>(() => new DateRange(new DateOnly(2024, 3, 1), new DateOnly(2024, 2, 1)));
        var first = new DateRange(new DateOnly(2024, 1, 1), new DateOnly(2024, 2, 1));
        var next = new DateRange(new DateOnly(2024, 2, 1), new DateOnly(2024, 3, 1));
        Assert.False(first.Overlaps(next));
        Assert.True(first.Overlaps(new DateRange(new DateOnly(2024, 1, 31), null)));
    }
}
