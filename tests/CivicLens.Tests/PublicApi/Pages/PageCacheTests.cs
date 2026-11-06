using CivicLens.PublicApi.Pages;
using CivicLens.Publication.Contracts;
using Microsoft.AspNetCore.Http;

namespace CivicLens.Tests.PublicApi.Pages;

public sealed class PageCacheTests
{
    private static readonly string Build = new('a', 40);
    private static readonly PublicBaseUrl Origin = PublicBaseUrl.Parse("https://civic-lens.test", "test")!;

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
}
