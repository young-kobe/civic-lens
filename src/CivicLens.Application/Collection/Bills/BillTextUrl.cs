using System.Text.RegularExpressions;

namespace CivicLens.Application.Collection.Bills;

public static partial class BillTextUrl
{
    private const string Suffix = "/htm";

    public static string Package(string sourceUrl, string packageId) => $"{sourceUrl}/{packageId}{Suffix}";

    public static string? TryReadPackageId(string sourceUrl, string url)
    {
        var prefix = sourceUrl + "/";
        if (!url.StartsWith(prefix, StringComparison.Ordinal) || !url.EndsWith(Suffix, StringComparison.Ordinal)) return null;
        var packageId = url[prefix.Length..^Suffix.Length];
        return PackageIdForm().IsMatch(packageId) ? packageId : null;
    }

    public static string ReadPackageId(string sourceUrl, string url) =>
        TryReadPackageId(sourceUrl, url) ?? throw new InvalidDataException("Job URL is not a bill text package URL.");

    public static bool IsPackageOf(string packageId, CongressBillListItem bill)
    {
        var prefix = $"BILLS-{bill.Congress}{bill.Type}{bill.Number}";
        return packageId.StartsWith(prefix, StringComparison.Ordinal) && packageId.Length > prefix.Length &&
            packageId[prefix.Length..].All(char.IsAsciiLetterLower);
    }

    [GeneratedRegex("^BILLS-[0-9]+[a-z]+[0-9]+[a-z]+$")]
    private static partial Regex PackageIdForm();
}
