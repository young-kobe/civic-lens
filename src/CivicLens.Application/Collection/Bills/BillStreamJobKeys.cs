using System.Globalization;
using CivicLens.Collection.Contracts;

namespace CivicLens.Application.Collection.Bills;

public static class BillStreamJobKeys
{
    public const string Prefix = "bill-stream/";

    public static string Detail(string sourceId, CongressBillListItem bill) =>
        $"{Prefix}{sourceId}/bill/{bill.Congress}/{bill.Type}/{bill.Number}/{bill.UpdateDate:yyyy-MM-dd}";

    public static bool IsDetailJobId(string? value)
    {
        if (value is null || value.Length > 512 || !value.StartsWith(Prefix, StringComparison.Ordinal)) return false;
        var parts = value[Prefix.Length..].Split('/');
        return parts.Length >= 6 && !string.IsNullOrWhiteSpace(string.Join('/', parts[..^5])) &&
            parts[^5] == "bill" &&
            CongressBillListItem.TryReadIdentity(parts[^4], parts[^3], parts[^2]) is { Congress: > 0, Number: > 0 } &&
            DateOnly.TryParseExact(parts[^1], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
    }

    public static string NextPage(string sourceId, string listJobId) => $"{Prefix}{sourceId}/page/{listJobId}";

    public static string FirstBillPage(string sourceId, ApiResource resource, CongressBillListItem bill,
        DateTimeOffset updateDate) =>
        $"{Prefix}{sourceId}/{Segment(resource)}/{bill.Congress}/{bill.Type}/{bill.Number}/" +
        ApiJobKeys.ExactTime(updateDate);

    public static string NextBillPage(string sourceId, ApiResource resource, string parentJobId) =>
        $"{Prefix}{sourceId}/{Segment(resource)}/page/{parentJobId}";

    public static string BillText(string sourceId, string versionsJobId) => $"{Prefix}{sourceId}/text/{versionsJobId}";

    private static string Segment(ApiResource resource) =>
        BillPageUrl.SegmentOf(resource) ?? throw new ArgumentOutOfRangeException(nameof(resource));
}
