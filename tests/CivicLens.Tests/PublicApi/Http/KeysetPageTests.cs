using CivicLens.PublicApi.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace CivicLens.Tests.PublicApi.Http;

public sealed class KeysetPageTests
{
    [Theory]
    [InlineData("v.voted_on DESC, v.chamber, v.record_id", "v.voted_on, v.chamber DESC, v.record_id DESC")]
    [InlineData("b.latest_action_date DESC NULLS LAST, b.record_id", "b.latest_action_date NULLS FIRST, b.record_id DESC")]
    [InlineData("x ASC NULLS FIRST, y NULLS LAST, z DESC NULLS FIRST", "x DESC NULLS LAST, y DESC, z")]
    [InlineData("COALESCE(b.introduced_on, DATE '0001-01-02') DESC, b.record_id", "COALESCE(b.introduced_on, DATE '0001-01-02'), b.record_id DESC")]
    [InlineData("lower(r.headline) ASC, r.record_id ASC", "lower(r.headline) DESC, r.record_id DESC")]
    public void APreviousPageReadsEachKeyInTheOtherDirectionWithTheNullsOnTheOtherSide(string order, string reversed)
    {
        Assert.Equal(reversed, KeysetPage.Order(order, backward: true));
        Assert.Equal(order, KeysetPage.Order(order, backward: false));
    }

    [Fact]
    public void ABeforeWindowLeavesOutTheRowsAfterTheCursorAndTheCursorRow()
    {
        Assert.Equal(" AND (x > @c)", KeysetPage.Window("x > @c", "v.record_id", "@cursor_id", backward: false));
        Assert.Equal(" AND (x > @c) IS NOT TRUE AND v.record_id <> @cursor_id", KeysetPage.Window("x > @c", "v.record_id", "@cursor_id", backward: true));
    }

    [Fact]
    public void AFirstPageHasNoPreviousCursorAndALaterPageLinksBackToItsFirstRow()
    {
        Assert.Equal(("1,2", "2", (string?)null), Cut([1, 2, 3], cursor: null, backward: false));
        Assert.Equal(("1,2", (string?)null, (string?)null), Cut([1, 2], cursor: null, backward: false));
        Assert.Equal(("4,5", "5", "4"), Cut([4, 5, 6], cursor: "3", backward: false));
        Assert.Equal(("4", (string?)null, "4"), Cut([4], cursor: "3", backward: false));
    }

    [Fact]
    public void APreviousPageFlipsTheReadAndHasAPreviousCursorOnlyWhenMoreRowsComeBeforeIt()
    {
        // The read of the rows before 5 comes in reverse order: 4, 3, and then 2, the row past the limit.
        Assert.Equal(("3,4", "4", "3"), Cut([4, 3, 2], cursor: "5", backward: true));
        Assert.Equal(("1,2", "2", (string?)null), Cut([2, 1], cursor: "3", backward: true));
    }

    [Fact]
    public void AnEmptyPageLinksToItsOwnCursorBecauseTheCursorRowLiesOnTheOtherSide()
    {
        Assert.Equal(("", (string?)null, "9"), Cut([], cursor: "9", backward: false));
        Assert.Equal(("", "1", (string?)null), Cut([], cursor: "1", backward: true));
    }

    [Fact]
    public void TheCursorComesFromTheNextOrThePreviousParameterAndBothTogetherAreRefused()
    {
        Assert.Equal(((string?)null, false), KeysetPage.Cursor(Query()));
        Assert.Equal(("v.1", false), KeysetPage.Cursor(Query(("cursor", "v.1"))));
        Assert.Equal(("v.1", true), KeysetPage.Cursor(Query(("before", "v.1"))));
        Assert.Equal("cursor", Assert.Throws<ArgumentException>(() => KeysetPage.Cursor(Query(("cursor", "v.1"), ("before", "v.2")))).ParamName);
        Assert.Equal("before", Assert.Throws<ArgumentException>(() => KeysetPage.Cursor(new QueryCollection(new Dictionary<string, StringValues> { ["before"] = new(["a", "b"]) }))).ParamName);
    }

    // The rows come back as one text, because a tuple compares an array by reference.
    private static (string Rows, string? Next, string? Previous) Cut(int[] rows, string? cursor, bool backward)
    {
        var (page, next, previous) = KeysetPage.Cut(rows, limit: 2, cursor, backward, row => row.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return (string.Join(',', page), next, previous);
    }

    private static QueryCollection Query(params (string Name, string Value)[] pairs) =>
        new(pairs.ToDictionary(pair => pair.Name, pair => new StringValues(pair.Value)));
}
