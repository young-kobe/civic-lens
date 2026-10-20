using System.Net;
using Microsoft.AspNetCore.Http;

namespace CivicLens.PublicApi.Http;

/// <summary>
/// Names the client of a request. Cloudflare sets <c>CF-Connecting-IP</c>, but any caller can send it.
/// The header counts only when the connection comes from a trusted proxy range.
/// </summary>
public sealed class ClientAddress(IReadOnlyList<IPNetwork> trustedProxyRanges)
{
    public const string ForwardedHeader = "CF-Connecting-IP";

    public string Of(HttpContext context)
    {
        var remote = context.Connection.RemoteIpAddress;
        if (remote is null) return "unknown";
        remote = Normalize(remote);
        var forwarded = context.Request.Headers[ForwardedHeader].ToString();
        return trustedProxyRanges.Any(range => range.Contains(remote)) && IPAddress.TryParse(forwarded, out var client)
            ? Key(Normalize(client))
            : Key(remote);
    }

    // A reader holds a whole IPv6 /64 network, so the limit counts a network and not one address.
    private static string Key(IPAddress address)
    {
        if (address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetworkV6) return address.ToString();
        var bytes = address.GetAddressBytes();
        Array.Clear(bytes, 8, 8);
        return new IPAddress(bytes).ToString() + "/64";
    }

    private static IPAddress Normalize(IPAddress address) => address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
}
