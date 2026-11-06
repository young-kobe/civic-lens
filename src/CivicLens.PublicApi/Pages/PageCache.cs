using System.Security.Cryptography;
using System.Text;
using CivicLens.Publication.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Headers;
using Microsoft.Net.Http.Headers;

namespace CivicLens.PublicApi.Pages;

/// <summary>The one owner of the cache rule of server pages. A page of one release, build, and public origin never changes. Its validator includes all three.
/// The edge keeps the page for 60 seconds, revalidates in the background for 10 minutes, and serves a kept copy for 24 hours when this service fails.
/// The rule holds no reader data: a page never varies by cookie or by query.</summary>
public sealed class PageCache(string buildId, PublicBaseUrl baseUrl)
{
    private readonly string originHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(baseUrl.Join(string.Empty))));

    public const string CacheControl = "public, s-maxage=60, stale-while-revalidate=600, stale-if-error=86400";

    public EntityTagHeaderValue ETag(int releaseNumber) => new($"\"r{releaseNumber}-{buildId}-{originHash}\"");

    public bool IsNotModified(HttpContext context, int releaseNumber)
    {
        var tag = ETag(releaseNumber);
        return context.Request.GetTypedHeaders().IfNoneMatch.Any(candidate =>
            candidate.Equals(EntityTagHeaderValue.Any) || candidate.Compare(tag, useStrongComparison: false));
    }

    /// <summary>Sets validators after a successful render, or when a matching request skips a successful page's render.</summary>
    public void Apply(HttpContext context, int releaseNumber)
    {
        var tag = ETag(releaseNumber);
        var response = context.Response.GetTypedHeaders();
        response.ETag = tag;
        context.Response.Headers.CacheControl = CacheControl;
    }
}
