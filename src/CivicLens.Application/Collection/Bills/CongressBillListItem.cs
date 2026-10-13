namespace CivicLens.Application.Collection.Bills;

public sealed record CongressBillListItem(int Congress, string Type, int Number, DateOnly UpdateDate)
{
    private static readonly HashSet<string> KnownTypes = new(StringComparer.Ordinal)
        { "hr", "s", "hjres", "sjres", "hconres", "sconres", "hres", "sres" };

    public static bool IsKnownType(string type) => KnownTypes.Contains(type);

    public string DetailUrl(string sourceUrl) => $"{sourceUrl}/{Congress}/{Type}/{Number}?format=json";
}
