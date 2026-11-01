namespace CivicLens.Application.Collection.Bills;

public sealed record CongressBillTextVersion(string PackageId, string Type, DateTimeOffset? Date, string Url)
{
    public const int MaximumTypeLength = 128;
    public const int MaximumUrlLength = 2048;
}
