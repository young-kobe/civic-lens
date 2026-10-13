using System.Globalization;
using CivicLens.Collection.Contracts;

namespace CivicLens.Application.Collection.Bills;

public static class BillStreamJobKeys
{
    public const string Prefix = "bill-stream/";
    private const string ExactTimeFormat = "yyyy-MM-dd'T'HH:mm:ss'Z'";

    public static string Detail(string sourceId, CongressBillListItem bill) =>
        $"{Prefix}{sourceId}/bill/{bill.Congress}/{bill.Type}/{bill.Number}/{bill.UpdateDate:yyyy-MM-dd}";

    public static string NextPage(string sourceId, string listJobId) => $"{Prefix}{sourceId}/page/{listJobId}";

    public static string FirstBillPage(string sourceId, ApiResource resource, CongressBillListItem bill,
        DateTimeOffset updateDate) =>
        $"{Prefix}{sourceId}/{Segment(resource)}/{bill.Congress}/{bill.Type}/{bill.Number}/" +
        updateDate.UtcDateTime.ToString(ExactTimeFormat, CultureInfo.InvariantCulture);

    public static string NextBillPage(string sourceId, ApiResource resource, string parentJobId) =>
        $"{Prefix}{sourceId}/{Segment(resource)}/page/{parentJobId}";

    private static string Segment(ApiResource resource) =>
        BillPageUrl.SegmentOf(resource) ?? throw new ArgumentOutOfRangeException(nameof(resource));
}
