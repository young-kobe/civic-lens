using CivicLens.PublicApi.Bills;

namespace CivicLens.Tests.PublicApi.Bills;

public sealed class ActionHistoryCursorTests
{
    [Fact]
    public void EncodesReleaseAndPositionUnderTheActionHistoryTag()
    {
        var cursor = new ActionHistoryCursor(12, 34);

        Assert.Equal("ba.12.34", cursor.Encode());
        Assert.Equal(cursor, ActionHistoryCursor.Parse(cursor.Encode()));
    }

    [Theory]
    [InlineData("p.12.34")]
    [InlineData("ba.0.34")]
    [InlineData("ba.12.-1")]
    [InlineData("ba.12.34.extra")]
    public void RefusesMalformedOrForeignCursors(string value)
    {
        Assert.Throws<ArgumentException>(() => ActionHistoryCursor.Parse(value));
    }
}
