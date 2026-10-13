namespace CivicLens.Application.Collection.Bills;

public static class BillStreamJobKeys
{
    public const string Prefix = "bill-stream/";

    public static string Detail(string sourceId, CongressBillListItem bill) =>
        $"{Prefix}{sourceId}/bill/{bill.Congress}/{bill.Type}/{bill.Number}/{bill.UpdateDate:yyyy-MM-dd}";

    public static string NextPage(string sourceId, string listJobId) => $"{Prefix}{sourceId}/page/{listJobId}";
}
