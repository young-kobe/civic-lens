using CivicLens.PublicApi.Pages;
using CivicLens.Publication.Contracts;
using Microsoft.AspNetCore.Http;

namespace CivicLens.Tests.PublicApi.Pages;

public sealed class PageCacheTests
{
    private static readonly string Build = new('a', 40);
    private static readonly PublicBaseUrl Origin = PublicBaseUrl.Parse("https://civic-lens.test", "test")!;

    [Fact]
    public void ThePageValidatorIsWeakBecauseItIdentifiesRenderedHtmlSemantics()
    {
        var cache = new PageCache(Build, Origin);

        Assert.True(cache.ETag(7).IsWeak);
        Assert.StartsWith("W/\"r7-" + Build + "-", cache.ETag(7).ToString());
        Assert.Equal("public, s-maxage=60, stale-while-revalidate=600, stale-if-error=86400", PageCache.CacheControl);
    }

    [Theory]
    [InlineData("https://other.test")]
    [InlineData("https://civic-lens.test:8443")]
    public void AChangedPublicOriginInvalidatesTheOldPageValidator(string changedOrigin)
    {
        var original = new PageCache(Build, Origin);
        var changed = new PageCache(Build, PublicBaseUrl.Parse(changedOrigin, "test")!);
        var context = new DefaultHttpContext();
        context.Request.Headers.IfNoneMatch = original.ETag(7).ToString();

        Assert.True(original.IsNotModified(context, 7));
        Assert.False(changed.IsNotModified(context, 7));
    }

    [Theory]
    [InlineData("https://CIVIC-LENS.test/")]
    [InlineData("https://civic-lens.test:443")]
    public void EquivalentNormalizedOriginsKeepTheSamePageValidator(string equivalentOrigin)
    {
        var original = new PageCache(Build, Origin);
        var equivalent = new PageCache(Build, PublicBaseUrl.Parse(equivalentOrigin, "test")!);
        var context = new DefaultHttpContext();
        context.Request.Headers.IfNoneMatch = original.ETag(7).ToString();

        Assert.True(equivalent.IsNotModified(context, 7));
    }

    [Fact]
    public void AChangedBrowserApiOriginInvalidatesTheHomePageValidator()
    {
        var original = new PageCache(Build, Origin, PublicBaseUrl.Parse("https://api.civic-lens.test", "test"));
        var changed = new PageCache(Build, Origin, PublicBaseUrl.Parse("https://api2.civic-lens.test", "test"));
        var context = new DefaultHttpContext();
        context.Request.Headers.IfNoneMatch = original.ETag(7).ToString();

        Assert.False(changed.IsNotModified(context, 7));
    }

    [Theory]
    [InlineData("https://API.civic-lens.test/")]
    [InlineData("https://api.civic-lens.test:443")]
    public void EquivalentNormalizedBrowserApiOriginsKeepTheSamePageValidator(string equivalentOrigin)
    {
        var original = new PageCache(Build, Origin, PublicBaseUrl.Parse("https://api.civic-lens.test", "test"));
        var equivalent = new PageCache(Build, Origin, PublicBaseUrl.Parse(equivalentOrigin, "test"));
        var context = new DefaultHttpContext();
        context.Request.Headers.IfNoneMatch = original.ETag(7).ToString();

        Assert.True(equivalent.IsNotModified(context, 7));
    }
}
