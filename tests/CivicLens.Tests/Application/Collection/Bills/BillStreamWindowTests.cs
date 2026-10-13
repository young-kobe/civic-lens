using CivicLens.Application.Collection.Bills;

namespace CivicLens.Tests.Application.Collection.Bills;

public sealed class BillStreamWindowTests
{
    private static readonly DateOnly Backfill = new(2025, 1, 3);

    [Theory]
    [InlineData("2025-01-05T05:59:59Z", "2025-01-03")]
    [InlineData("2025-01-05T06:00:00Z", "2025-01-04")]
    public void ADayClosesSixHoursAfterItEndsSoLocalTimeEdgesCannotHideLateUpdates(string now, string lastClosed)
    {
        Assert.Equal(DateOnly.Parse(lastClosed), BillStreamWindow.LastClosedDay(DateTimeOffset.Parse(now)));
    }

    [Fact]
    public void NothingIsDueWhileTheNextDayIsStillOpen()
    {
        var window = BillStreamWindow.Next(null, Backfill, DateTimeOffset.Parse("2025-01-04T05:00:00Z"));

        Assert.Null(window);
    }

    [Fact]
    public void FirstWindowStartsAtTheBackfillDateAndOtherWindowsStartAfterTheCursor()
    {
        var now = DateTimeOffset.Parse("2025-01-05T06:00:00Z");

        Assert.Equal(new BillStreamWindow(Backfill, new DateOnly(2025, 1, 4)), BillStreamWindow.Next(null, Backfill, now));
        Assert.Equal(new BillStreamWindow(new DateOnly(2025, 1, 4), new DateOnly(2025, 1, 4)),
            BillStreamWindow.Next(new DateOnly(2025, 1, 3), Backfill, now));
        Assert.Null(BillStreamWindow.Next(new DateOnly(2025, 1, 4), Backfill, now));
    }

    [Fact]
    public void ABackfillWindowCoversAtMostSevenClosedDays()
    {
        var window = BillStreamWindow.Next(null, Backfill, DateTimeOffset.Parse("2025-03-01T12:00:00Z"))!.Value;

        Assert.Equal(new DateOnly(2025, 1, 9), window.LastDay);
        Assert.True(window.IsBehind(DateTimeOffset.Parse("2025-03-01T12:00:00Z")));
    }

    [Fact]
    public void TheRequestStartsOneDayBeforeTheFirstDayToOverlapTheLastWindow()
    {
        var window = new BillStreamWindow(Backfill, new DateOnly(2025, 1, 9));

        Assert.Equal("https://api.congress.gov/v3/bill?fromDateTime=2025-01-02T00:00:00Z&toDateTime=2025-01-10T00:00:00Z&sort=updateDate+asc&limit=250&format=json",
            window.RequestUrl("https://api.congress.gov/v3/bill"));
    }

    [Fact]
    public void AWindowRoundTripsThroughItsRequestUrlAndDetailUrlsCarryNoWindow()
    {
        var window = new BillStreamWindow(Backfill, new DateOnly(2025, 1, 9));

        Assert.Equal(window, BillStreamWindow.TryRead(window.RequestUrl("https://api.congress.gov/v3/bill")));
        Assert.Null(BillStreamWindow.TryRead("https://api.congress.gov/v3/bill/119/hr/1?format=json"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("&sort=updateDate+desc")]
    public void AListUrlWithoutOldestUpdateFirstOrderHasNoWindowBecauseItsPagesCouldSkipBills(string sort)
    {
        var url = "https://api.congress.gov/v3/bill?fromDateTime=2025-01-02T00:00:00Z&toDateTime=2025-01-10T00:00:00Z" +
            sort + "&offset=250&limit=250&format=json";

        Assert.Null(BillStreamWindow.TryRead(url));
    }

    [Fact]
    public void TheNextLinkFormOfTheSortStillNamesTheWindow()
    {
        var url = "https://api.congress.gov/v3/bill?fromDateTime=2025-01-02T00:00:00Z&sort=updateDate asc" +
            "&toDateTime=2025-01-10T00:00:00Z&offset=250&limit=250&format=json";

        Assert.Equal(new BillStreamWindow(Backfill, new DateOnly(2025, 1, 9)), BillStreamWindow.TryRead(url));
    }

    [Theory]
    [InlineData("&offset=250&limit=100000&format=json")]
    [InlineData("&offset=250&limit=250&format=xml")]
    [InlineData("&offset=250&limit=250&format=json&api_key=DEMO_KEY")]
    [InlineData("&offset=-250&limit=250&format=json")]
    [InlineData("&offset=250&offset=500&limit=250&format=json")]
    [InlineData("&offset=0250&limit=250&format=json")]
    [InlineData("&offset=00&limit=250&format=json")]
    [InlineData("&limit=250&format=json&congress")]
    public void AListUrlWithAnyOtherParameterHasNoWindowSoAJobCannotWidenItsPageOrCarryACredential(string parameters)
    {
        var url = "https://api.congress.gov/v3/bill?fromDateTime=2025-01-02T00:00:00Z&sort=updateDate asc" +
            "&toDateTime=2025-01-10T00:00:00Z" + parameters;

        Assert.Null(BillStreamWindow.TryRead(url));
    }

    [Fact]
    public void OnlyThePageWithoutAnOffsetStartsAPassSoLaterPagesAddToItsCount()
    {
        var first = new BillStreamWindow(Backfill, Backfill).RequestUrl("https://api.congress.gov/v3/bill");

        Assert.True(BillStreamWindow.StartsPass(first));
        Assert.True(BillStreamWindow.StartsPass(first + "&offset=0"));
        Assert.False(BillStreamWindow.StartsPass(first + "&offset=250"));
    }

    [Fact]
    public void ThePageThatEndsOnTheLastClosedDayIsNotBehind()
    {
        var now = DateTimeOffset.Parse("2025-01-05T06:00:00Z");

        Assert.False(new BillStreamWindow(Backfill, new DateOnly(2025, 1, 4)).IsBehind(now));
    }
}
