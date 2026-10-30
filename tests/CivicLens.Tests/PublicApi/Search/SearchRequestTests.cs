using CivicLens.PublicApi.Search;
using CivicLens.Publication.Contracts;
using Microsoft.AspNetCore.Http;

namespace CivicLens.Tests.PublicApi.Search;

public sealed class SearchRequestTests
{
    private static SearchRequest Parse(string query) =>
        SearchRequest.Parse(new QueryCollection(Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(query)));

    [Fact]
    public void AnEmptyQueryStringMeansADateOrderedSearchOfTheActiveReleaseWithTheDefaultLimit()
    {
        var request = Parse("");

        Assert.False(request.IsRanked);
        Assert.Null(request.Release);
        Assert.Equal(SearchRequest.DefaultLimit, request.Limit);
    }

    [Fact]
    public void TextIsTrimmedAndBlankTextMeansNoText()
    {
        Assert.Equal("rent cap", Parse("q=%20rent%20cap%20").Text);
        Assert.Null(Parse("q=%20%20").Text);
    }

    [Fact]
    public void EveryFilterIsReadAsOneValue()
    {
        var request = Parse("kind=action&official=o1&issue=housing&policyArea=area&source=a.gov&from=2026-01-02&to=2026-03-04&release=5&limit=7");

        Assert.Equal((PublishedRecordKind.Action, "o1", "housing", "area", "a.gov"), (request.Kind, request.Official, request.Issue, request.PolicyArea, request.Source));
        Assert.Equal((new DateOnly(2026, 1, 2), new DateOnly(2026, 3, 4), 5, 7), (request.From, request.To, request.Release, request.Limit));
    }

    [Fact]
    public void ACursorPinsItsReleaseAndANamedReleaseThatDiffersIsRefused()
    {
        var cursor = SearchCursor.Dated(4, new string('a', 64), new DateOnly(2026, 1, 2), 100).Encode();

        Assert.Equal(4, Parse("cursor=" + cursor).Release);
        Assert.Throws<ArgumentException>(() => Parse($"release=3&cursor={cursor}"));
    }

    [Fact]
    public void ARankedCursorDoesNotFitASearchWithoutTextAndTheOtherWayRound()
    {
        var ranked = SearchCursor.Ranked(1, new string('a', 64), 0.5f).Encode();
        var dated = SearchCursor.Dated(1, new string('a', 64), new DateOnly(2026, 1, 2), 100).Encode();

        Assert.Throws<ArgumentException>(() => Parse("cursor=" + ranked));
        Assert.Throws<ArgumentException>(() => Parse("q=rent&cursor=" + dated));
        Assert.Equal(0.5f, Parse("q=rent&cursor=" + ranked).Cursor!.Rank);
    }

    [Fact]
    public void TheCursorRoundTripsTheRankExactlyBecauseTheKeysetCompareUsesEquality()
    {
        var rank = 0.0607927f;

        Assert.Equal(rank, SearchCursor.Parse(SearchCursor.Ranked(2, new string('a', 64), rank).Encode())!.Rank);
    }

    [Fact]
    public void WithoutASortTheOrderStaysTheOldOneSoOldClientsAndTheirCursorsKeepWorking()
    {
        var old = Parse("cursor=d.4.20260102.100." + new string('a', 64));

        Assert.Null(old.Sort);
        Assert.False(old.IsRanked);
        Assert.True(Parse("q=rent").IsRanked);
    }

    [Theory]
    [InlineData("sort=date", SearchSortKey.Date, true)]
    [InlineData("sort=published", SearchSortKey.Published, true)]
    [InlineData("sort=headline", SearchSortKey.Headline, false)]
    [InlineData("sort=kind", SearchSortKey.Kind, false)]
    [InlineData("sort=date&direction=asc", SearchSortKey.Date, false)]
    [InlineData("sort=headline&direction=desc", SearchSortKey.Headline, true)]
    public void ASortKeyTakesTheDirectionThatReadsNaturallyUnlessTheRequestNamesOne(string query, SearchSortKey key, bool descending)
    {
        var request = Parse(query);

        Assert.Equal((key, descending), (request.Sort, request.Descending));
    }

    [Fact]
    public void ANamedSortOverridesTheRankOfASearchWithText()
    {
        Assert.False(Parse("q=rent&sort=headline").IsRanked);
    }

    [Fact]
    public void ACursorMustFitTheOrderOfTheSearchBecauseAnotherFormMeansAnotherKeyset()
    {
        var id = new string('a', 64);
        var sorted = SearchCursor.Sorted(1, id).Encode();
        var dated = SearchCursor.Dated(1, id, new DateOnly(2026, 1, 2), 100).Encode();
        var ranked = SearchCursor.Ranked(1, id, 0.5f).Encode();

        Assert.Equal($"s.1.{id}", sorted);
        Assert.True(Parse("sort=kind&cursor=" + sorted).Cursor!.IsSorted);
        Assert.True(Parse("q=rent&sort=kind&cursor=" + sorted).Cursor!.IsSorted);
        Assert.Throws<ArgumentException>(() => Parse("cursor=" + sorted));
        Assert.Throws<ArgumentException>(() => Parse("q=rent&cursor=" + sorted));
        Assert.Throws<ArgumentException>(() => Parse("sort=kind&cursor=" + dated));
        Assert.Throws<ArgumentException>(() => Parse("sort=kind&cursor=" + ranked));
    }

    [Theory]
    [InlineData("x")]
    [InlineData("s.1")]
    [InlineData("s.1.NOTHEX")]
    [InlineData("s.x.aa")]
    [InlineData("r.1.5")]
    [InlineData("r.1.-5.aa")]
    [InlineData("d.1.20260230.5.aa")]
    [InlineData("d.1.20260101.5.NOTHEX")]
    public void AMalformedCursorIsRefused(string cursor)
    {
        Assert.Throws<ArgumentException>(() => SearchCursor.Parse(cursor));
    }
}
