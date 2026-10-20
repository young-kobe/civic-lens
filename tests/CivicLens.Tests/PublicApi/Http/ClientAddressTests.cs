using System.Net;
using CivicLens.PublicApi.Http;
using Microsoft.AspNetCore.Http;

namespace CivicLens.Tests.PublicApi.Http;

public sealed class ClientAddressTests
{
    private static readonly IPNetwork[] Cloudflare = [IPNetwork.Parse("173.245.48.0/20"), IPNetwork.Parse("2400:cb00::/32")];

    private static HttpContext Request(string remote, string? forwarded)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(remote);
        if (forwarded is not null) context.Request.Headers[ClientAddress.ForwardedHeader] = forwarded;
        return context;
    }

    [Fact]
    public void TheForwardedHeaderNamesTheClientWhenTheConnectionComesFromACloudflareRange()
    {
        Assert.Equal("198.51.100.7", new ClientAddress(Cloudflare).Of(Request("173.245.48.5", "198.51.100.7")));
    }

    [Fact]
    public void TheForwardedHeaderIsIgnoredWhenTheConnectionComesFromOutsideTheCloudflareRangesBecauseAnyCallerCanSendIt()
    {
        Assert.Equal("203.0.113.9", new ClientAddress(Cloudflare).Of(Request("203.0.113.9", "198.51.100.7")));
    }

    [Fact]
    public void WithNoConfiguredRangesTheRemoteAddressIsTheClientEvenWhenTheHeaderIsPresent()
    {
        Assert.Equal("127.0.0.1", new ClientAddress([]).Of(Request("127.0.0.1", "198.51.100.7")));
    }

    [Fact]
    public void ATrustedConnectionWithAMissingOrBadHeaderFallsBackToTheRemoteAddress()
    {
        var clients = new ClientAddress(Cloudflare);

        Assert.Equal("173.245.48.5", clients.Of(Request("173.245.48.5", null)));
        Assert.Equal("173.245.48.5", clients.Of(Request("173.245.48.5", "not an address")));
    }

    [Fact]
    public void AllAddressesOfOneIpv6Slash64AreOneClientBecauseAReaderHoldsTheWholeNetwork()
    {
        var clients = new ClientAddress([]);

        Assert.Equal(clients.Of(Request("2001:db8:1:2::1", null)), clients.Of(Request("2001:db8:1:2:ffff::9", null)));
        Assert.NotEqual(clients.Of(Request("2001:db8:1:2::1", null)), clients.Of(Request("2001:db8:1:3::1", null)));
    }

    [Fact]
    public void AnIpv4AddressInIpv6FormIsTheSameClientAsItsIpv4Form()
    {
        Assert.Equal("173.245.48.5", new ClientAddress(Cloudflare).Of(Request("::ffff:173.245.48.5", null)));
    }
}
