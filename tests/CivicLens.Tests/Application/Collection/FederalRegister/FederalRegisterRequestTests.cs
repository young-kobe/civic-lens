using CivicLens.Application.Collection.FederalRegister;
using CivicLens.Tests.Fixtures;

namespace CivicLens.Tests.Application.Collection.FederalRegister;

public sealed class FederalRegisterRequestTests
{
    private const string Source = FederalRegisterFixtures.SourceUrl;
    private static readonly DateOnly Backfill = FederalRegisterFixtures.Backfill;

    [Fact]
    public void ThePassedWindowReadsBackFromItsListUrlAndNoOtherUrlHasTheForm()
    {
        var window = new FederalRegisterWindow(new DateOnly(2025, 1, 26), new DateOnly(2025, 1, 29));
        var url = FederalRegisterUrl.List(Source, window);

        Assert.Equal(window, FederalRegisterUrl.TryReadList(Source, url));
        Assert.Null(FederalRegisterUrl.TryReadList(Source, url + "&api_key=secret"));
        Assert.Null(FederalRegisterUrl.TryReadList(Source, url.Replace("PRESDOCU", "RULE")));
        Assert.Null(FederalRegisterUrl.TryReadList(Source, url.Replace("&order=oldest", "&order=newest")));
        Assert.Null(FederalRegisterUrl.TryReadList(Source, FederalRegisterUrl.List(Source, window with { First = window.Last.AddDays(1) })));
    }

    [Fact]
    public void ADocumentUrlPinsTheFieldsSoAChangingPageViewCountNeverEntersACapture()
    {
        var url = FederalRegisterUrl.Document(Source, "2025-01900");

        Assert.StartsWith("https://www.federalregister.gov/api/v1/documents/2025-01900.json?fields%5B%5D=document_number", url);
        Assert.DoesNotContain("page_views", url);
        Assert.Equal("2025-01900", FederalRegisterUrl.TryReadDocument(Source, url));
        Assert.Null(FederalRegisterUrl.TryReadDocument(Source, url + "&fields%5B%5D=page_views"));
        Assert.Null(FederalRegisterUrl.TryReadDocument(Source, url.Replace("2025-01900", "../2025-01900")));
    }

    [Fact]
    public void WithNoDocumentYetTheWindowStartsAtTheBackfillDate()
    {
        var window = FederalRegisterWindow.Next(null, Backfill, new DateOnly(2026, 10, 29));

        Assert.Equal(new FederalRegisterWindow(Backfill, new DateOnly(2026, 10, 29)), window);
    }

    [Fact]
    public void AfterADocumentTheWindowStartsTwoDaysBeforeItsPublicationSoALateListingIsNotMissed()
    {
        var window = FederalRegisterWindow.Next(new DateOnly(2026, 10, 27), Backfill, new DateOnly(2026, 10, 29));

        Assert.Equal(new FederalRegisterWindow(new DateOnly(2026, 10, 25), new DateOnly(2026, 10, 29)), window);
    }

    [Fact]
    public void TheWindowNeverStartsBeforeTheBackfillDate()
    {
        var window = FederalRegisterWindow.Next(Backfill.AddDays(1), Backfill, new DateOnly(2026, 10, 29));

        Assert.Equal(Backfill, window!.Value.First);
    }

    [Fact]
    public void ABackfillDateInTheFutureMakesNoWindow()
    {
        Assert.Null(FederalRegisterWindow.Next(null, new DateOnly(2026, 10, 30), new DateOnly(2026, 10, 29)));
    }
}
