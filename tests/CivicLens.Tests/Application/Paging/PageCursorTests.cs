using CivicLens.Application.Paging;

namespace CivicLens.Tests.Application.Paging;

public sealed class PageCursorTests
{
    [Fact]
    public void CursorRoundTripsDirectionTimeAndId()
    {
        var cursor = new PageCursor(PageDirection.Newer, 639_000_000_000_000_000, "0123abcd-EF_9");

        Assert.Equal(cursor, PageCursor.Parse(cursor.Encode()));
    }

    [Fact]
    public void MissingCursorMeansTheFirstPage()
    {
        Assert.Null(PageCursor.Parse(null));
    }

    // A client can edit a cursor. Anything that is not exactly what the server issues is invalid input.
    [Theory]
    [InlineData("")]
    [InlineData("x.1.abc")]
    [InlineData("o.1")]
    [InlineData("o..abc")]
    [InlineData("o.1.")]
    [InlineData("o.-1.abc")]
    [InlineData("o.+1.abc")]
    [InlineData("o.1.2.3.abc")]
    [InlineData("o.1.a b")]
    [InlineData("o.1.a'--")]
    [InlineData("o.99999999999999999999.abc")]
    [InlineData("O.1.abc")]
    public void TamperedCursorIsRejectedAsInvalidInput(string value)
    {
        Assert.Throws<ArgumentException>(() => PageCursor.Parse(value));
    }

    [Fact]
    public void RankedCursorRoundTripsAndTheTwoPartFormStillParses()
    {
        var ranked = new PageCursor(PageDirection.Older, 639_000_000_000_000_000, "abc", 2);

        Assert.Equal("o.2.639000000000000000.abc", ranked.Encode());
        Assert.Equal(ranked, PageCursor.Parse(ranked.Encode()));
        Assert.Equal(new PageCursor(PageDirection.Newer, 7, "abc"), PageCursor.Parse("n.7.abc"));
        Assert.Null(PageCursor.Parse("n.7.abc")!.Rank);
    }

    // The rank is the review tier, so a client cannot ask for a tier that does not exist.
    [Theory]
    [InlineData("o.0.2.abc")]
    [InlineData("o.4.2.abc")]
    [InlineData("o.-1.2.abc")]
    [InlineData("o..2.abc")]
    [InlineData("o.x.2.abc")]
    [InlineData("o.99999999999.2.abc")]
    public void ABadRankIsRejectedAsInvalidInput(string value)
    {
        Assert.Throws<ArgumentException>(() => PageCursor.Parse(value));
    }

    [Fact]
    public void RankedSliceKeepsTheRankInItsCursorsAndOnAnEmptyPage()
    {
        var cursor = new PageCursor(PageDirection.Older, 20, "b", 2);

        var slice = KeysetSlice<(int Rank, long Ticks, string Id)>.Create([(3, 10L, "a")], 2, cursor, RankedKey);
        var empty = KeysetSlice<(int Rank, long Ticks, string Id)>.Create([], 2, cursor, RankedKey);

        Assert.Equal(new PageCursor(PageDirection.Newer, 10, "a", 3), PageCursor.Parse(slice.NewerCursor));
        Assert.Null(slice.OlderCursor);
        Assert.Equal(new PageCursor(PageDirection.Newer, 20, "b", 2), PageCursor.Parse(empty.NewerCursor));
    }

    [Fact]
    public void OverlongCursorAndIdAreRejected()
    {
        Assert.Throws<ArgumentException>(() => PageCursor.Parse("o.1." + new string('a', 129)));
        Assert.Throws<ArgumentException>(() => PageCursor.Parse(new string('o', 257)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void LimitOutsideBoundsIsRejected(int limit)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PageLimit.Validate(limit));
    }

    [Fact]
    public void FirstPageHasOnlyAnOlderCursorWhenMoreRowsExist()
    {
        var slice = KeysetSlice<(long Ticks, string Id)>.Create([(30L, "c"), (20L, "b"), (10L, "a")], 2, null, Key);

        Assert.Equal(new (long, string)[] { (30L, "c"), (20L, "b") }, slice.Items.ToArray());
        Assert.Null(slice.NewerCursor);
        Assert.Equal(new PageCursor(PageDirection.Older, 20, "b"), PageCursor.Parse(slice.OlderCursor));
    }

    [Fact]
    public void LastPageHasOnlyANewerCursor()
    {
        var cursor = new PageCursor(PageDirection.Older, 20, "b");

        var slice = KeysetSlice<(long Ticks, string Id)>.Create([(10L, "a")], 2, cursor, Key);

        Assert.Equal(new PageCursor(PageDirection.Newer, 10, "a"), PageCursor.Parse(slice.NewerCursor));
        Assert.Null(slice.OlderCursor);
    }

    [Fact]
    public void MovingNewerReturnsRowsNewestFirstAndKeepsABackLink()
    {
        // The store reads toward newer rows oldest first, so the slice must flip them back to newest first.
        var cursor = new PageCursor(PageDirection.Newer, 10, "a");

        var slice = KeysetSlice<(long Ticks, string Id)>.Create([(20L, "b"), (30L, "c"), (40L, "d")], 2, cursor, Key);

        Assert.Equal(new (long, string)[] { (30L, "c"), (20L, "b") }, slice.Items.ToArray());
        Assert.Equal(new PageCursor(PageDirection.Newer, 30, "c"), PageCursor.Parse(slice.NewerCursor));
        Assert.Equal(new PageCursor(PageDirection.Older, 20, "b"), PageCursor.Parse(slice.OlderCursor));
    }

    [Fact]
    public void EmptyPageKeepsTheRequestPositionSoTheClientCanStillMoveBack()
    {
        var cursor = new PageCursor(PageDirection.Older, 5, "z");

        var slice = KeysetSlice<(long Ticks, string Id)>.Create([], 2, cursor, Key);

        Assert.Empty(slice.Items);
        Assert.Equal(new PageCursor(PageDirection.Newer, 5, "z"), PageCursor.Parse(slice.NewerCursor));
        Assert.Null(slice.OlderCursor);
    }

    [Fact]
    public void EmptyFirstPageHasNoCursors()
    {
        var slice = KeysetSlice<(long Ticks, string Id)>.Create([], 2, null, Key);

        Assert.Null(slice.NewerCursor);
        Assert.Null(slice.OlderCursor);
    }

    private static (int?, long, string) RankedKey((int Rank, long Ticks, string Id) row) => (row.Rank, row.Ticks, row.Id);

    private static (long, string) Key((long Ticks, string Id) row) => row;
}
