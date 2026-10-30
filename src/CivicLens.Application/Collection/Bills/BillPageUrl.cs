using CivicLens.Collection.Contracts;

namespace CivicLens.Application.Collection.Bills;

public readonly record struct BillPageUrl(CongressBillListItem Bill, int Offset)
{
    public const int SummariesPageLimit = 1;

    public static int LimitOf(ApiResource resource) =>
        resource == ApiResource.BillSummaries ? SummariesPageLimit : ApiPageQuery.PageLimit;

    public static string FirstPage(string sourceUrl, ApiResource resource, CongressBillListItem bill)
    {
        ArgumentNullException.ThrowIfNull(bill);
        var segment = SegmentOf(resource) ?? throw new ArgumentOutOfRangeException(nameof(resource));
        return $"{sourceUrl}/{bill.Congress}/{bill.Type}/{bill.Number}/{segment}?{ApiPageQuery.FirstPage(LimitOf(resource))}";
    }

    public static BillPageUrl? TryRead(string sourceUrl, ApiResource resource, string? url)
    {
        if (url is null || SegmentOf(resource) is not { } segment) return null;
        if (ApiPageQuery.TryReadPage(sourceUrl, url, LimitOf(resource)) is not var (parts, offset)) return null;
        if (parts.Length != 4 || parts[3] != segment) return null;
        var bill = CongressBillListItem.TryReadIdentity(parts[0], parts[1], parts[2]);
        return bill is null ? null : new BillPageUrl(bill, offset);
    }

    internal static string? ReadNext(string? next, CollectionRequest scope, string sourceUrl, ApiResource resource,
        CongressBillListItem expected)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var current = TryRead(sourceUrl, resource, scope.Url)
            ?? throw new InvalidDataException("Current page URL is not a page of this bill resource.");
        if (!current.Bill.IsSameBillAs(expected))
            throw new InvalidDataException("Current page URL is for another bill than expected.");
        return ApiPageQuery.ReadNext(next, scope, IdentityOf(current.Bill), current.Offset,
            url => TryRead(sourceUrl, resource, url) is { } page ? (IdentityOf(page.Bill), page.Offset) : null,
            "Next page link is for another bill.");
    }

    private static (int Congress, string Type, int Number) IdentityOf(CongressBillListItem bill) =>
        (bill.Congress, bill.Type, bill.Number);

    internal static string? SegmentOf(ApiResource resource) => resource switch
    {
        ApiResource.BillCosponsors => "cosponsors",
        ApiResource.BillActions => "actions",
        ApiResource.BillSummaries => "summaries",
        ApiResource.BillSubjects => "subjects",
        _ => null
    };
}
