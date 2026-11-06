using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Headers;
using Microsoft.Net.Http.Headers;

namespace CivicLens.PublicApi.Pages;

/// <summary>The one owner of the cache rule of server pages. A page of one release and one build never changes, so its ETag is <c>r{release}-{build}</c>.
/// The edge keeps the page for 60 seconds, revalidates in the background for 10 minutes, and serves a kept copy for 24 hours when this service fails.
/// The rule holds no reader data: a page never varies by cookie or by query.</summary>
public sealed class PageCache(string buildId)
{
    public const string CacheControl = "public, s-maxage=60, stale-while-revalidate=600, stale-if-error=86400";

    public EntityTagHeaderValue ETag(int releaseNumber) => new($"\"r{releaseNumber}-{buildId}\"");

    /// <summary>Sets the validators on the response. Returns true when the request already holds this page, so the caller answers 304.</summary>
    public bool Apply(HttpContext context, int releaseNumber)
    {
        var tag = ETag(releaseNumber);
        var response = context.Response.GetTypedHeaders();
        response.ETag = tag;
        context.Response.Headers.CacheControl = CacheControl;
        return context.Request.GetTypedHeaders().IfNoneMatch.Any(candidate => candidate.Compare(tag, useStrongComparison: false));
    }
}
