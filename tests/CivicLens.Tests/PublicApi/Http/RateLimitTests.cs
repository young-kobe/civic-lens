using System.Net;
using CivicLens.PublicApi.Http;
using CivicLens.Tests.Infrastructure.Collection;

namespace CivicLens.Tests.PublicApi.Http;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class RateLimitTests(PostgresCollection postgres)
{
    [Fact]
    public async Task TheRateLimitCountsEachForwardedClientSeparatelyWhenTheConnectionIsTrusted()
    {
        await using var host = await ApiHost.StartAsync(postgres, options => options with
        {
            RequestsPerMinute = 2,
            TrustedProxyRanges = [IPNetwork.Parse("127.0.0.0/8")]
        });

        Assert.Equal([HttpStatusCode.ServiceUnavailable, HttpStatusCode.ServiceUnavailable, HttpStatusCode.TooManyRequests],
            await StatusesAsync(host, "198.51.100.1", 3));
        Assert.Equal([HttpStatusCode.ServiceUnavailable, HttpStatusCode.ServiceUnavailable],
            await StatusesAsync(host, "198.51.100.2", 2));
    }

    [Fact]
    public async Task TheRateLimitIgnoresAForgedHeaderFromAnUntrustedConnectionSoRotatingItBuysNoMoreRequests()
    {
        await using var host = await ApiHost.StartAsync(postgres, options => options with
        {
            RequestsPerMinute = 2,
            TrustedProxyRanges = [IPNetwork.Parse("203.0.113.0/24")]
        });

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 3; i++) statuses.AddRange(await StatusesAsync(host, "198.51.100." + (i + 1), 1));

        Assert.Equal([HttpStatusCode.ServiceUnavailable, HttpStatusCode.ServiceUnavailable, HttpStatusCode.TooManyRequests], statuses);
    }

    [Fact]
    public async Task TheZipRouteCountsAgainstTheGlobalLimitLikeTheVoteRoutes()
    {
        await using var host = await ApiHost.StartAsync(postgres, options => options with { RequestsPerMinute = 2 });

        Assert.Equal([HttpStatusCode.ServiceUnavailable, HttpStatusCode.ServiceUnavailable, HttpStatusCode.TooManyRequests],
            await StatusesAsync(host, "198.51.100.1", 3, "/v1/zip-districts/99501"));
    }

    private static async Task<List<HttpStatusCode>> StatusesAsync(ApiHost host, string forwarded, int count, string path = "/v1/release")
    {
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < count; i++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.Add(ClientAddress.ForwardedHeader, forwarded);
            statuses.Add((await host.Client.SendAsync(request)).StatusCode);
        }
        return statuses;
    }
}
