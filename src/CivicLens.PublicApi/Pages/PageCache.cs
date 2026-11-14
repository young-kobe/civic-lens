using System.Security.Cryptography;
using System.Text;
using CivicLens.Publication.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Headers;
using Microsoft.Net.Http.Headers;

namespace CivicLens.PublicApi.Pages;

/// <summary>The owner of shared freshness and the weak semantic validator of server pages. A page of one release, build, site origin, browser API origin, and Turnstile site key never changes.
/// The edge keeps the page for 60 seconds, revalidates in the background for 10 minutes, and serves a kept copy for 24 hours when this service fails.
/// A page with a status other than success, such as the not found page, gets no validator and no cache keeps it, as the static 404 page.
/// Caddy owns transport encoding and no-transform at ingress. The rule holds no reader data: a page never varies by cookie or by query.</summary>
public sealed class PageCache(string buildId, PublicBaseUrl baseUrl, PublicBaseUrl? browserApiUrl = null, string? turnstileSiteKey = null)
{
    private readonly string originHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
        $"{baseUrl.Join(string.Empty)}\n{browserApiUrl?.Join(string.Empty) ?? string.Empty}\n{turnstileSiteKey ?? string.Empty}")));

    public const string CacheControl = "public, s-maxage=60, stale-while-revalidate=600, stale-if-error=86400";
    public const string FailureCacheControl = "no-store";

    public EntityTagHeaderValue ETag(int releaseNumber) => new($"\"r{releaseNumber}-{buildId}-{originHash}\"", isWeak: true);

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

    /// <summary>Sets the cache value of a rendered page with a status other than success. It sends no validator.</summary>
    public static void ApplyFailure(HttpContext context) => context.Response.Headers.CacheControl = FailureCacheControl;
}
