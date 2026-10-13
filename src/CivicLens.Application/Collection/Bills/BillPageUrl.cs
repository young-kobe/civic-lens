using System.Web;
using CivicLens.Collection.Contracts;

namespace CivicLens.Application.Collection.Bills;

public readonly record struct BillPageUrl(CongressBillListItem Bill, int Offset)
{
    public static string FirstPage(string sourceUrl, ApiResource resource, CongressBillListItem bill)
    {
        ArgumentNullException.ThrowIfNull(bill);
        var segment = SegmentOf(resource) ?? throw new ArgumentOutOfRangeException(nameof(resource));
        return $"{sourceUrl}/{bill.Congress}/{bill.Type}/{bill.Number}/{segment}?{BillPageQuery.FirstPage}";
    }

    public static BillPageUrl? TryRead(string sourceUrl, ApiResource resource, string? url)
    {
        if (url is null) return null;
        var segment = SegmentOf(resource);
        var prefix = sourceUrl + "/";
        var queryStart = url.IndexOf('?', StringComparison.Ordinal);
        if (segment is null || queryStart < 0 || !url.StartsWith(prefix, StringComparison.Ordinal)) return null;
        var rawQuery = url[(queryStart + 1)..];
        if (rawQuery.Contains('%') || rawQuery.Contains('+')) return null;
        var parts = url[prefix.Length..queryStart].Split('/');
        var query = HttpUtility.ParseQueryString(rawQuery);
        if (parts.Length != 4 || parts[3] != segment || !BillPageQuery.IsValid(query)) return null;
        var bill = CongressBillListItem.TryReadIdentity(parts[0], parts[1], parts[2]);
        return bill is null ? null : new BillPageUrl(bill, BillPageQuery.ReadOffset(query)!.Value);
    }

    internal static string? ReadNext(string? next, CollectionRequest scope, string sourceUrl, ApiResource resource,
        CongressBillListItem expected)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var current = TryRead(sourceUrl, resource, scope.Url)
            ?? throw new InvalidDataException("Current page URL is not a page of this bill resource.");
        if (!current.Bill.IsSameBillAs(expected))
            throw new InvalidDataException("Current page URL is for another bill than expected.");
        if (string.IsNullOrEmpty(next)) return null;
        if (!Uri.TryCreate(next, UriKind.Absolute, out var uri) || !scope.Allows(uri))
            throw new InvalidDataException("Next page link is outside the source scope.");
        var following = TryRead(sourceUrl, resource, next)
            ?? throw new InvalidDataException("Next page link does not have the exact page form.");
        if (!following.Bill.IsSameBillAs(expected))
            throw new InvalidDataException("Next page link is for another bill.");
        if (following.Offset <= current.Offset)
            throw new InvalidDataException("Next page link does not advance the offset.");
        return next;
    }

    internal static string? SegmentOf(ApiResource resource) => resource switch
    {
        ApiResource.BillCosponsors => "cosponsors",
        ApiResource.BillActions => "actions",
        _ => null
    };
}
