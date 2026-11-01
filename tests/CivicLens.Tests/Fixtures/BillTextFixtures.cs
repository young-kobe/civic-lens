using CivicLens.Application.Collection;
using CivicLens.Application.Collection.Bills;
using CivicLens.Application.Collection.Discovery;
using CivicLens.Collection.Contracts;
using CivicLens.Tests.Application.Collection.Bills;

namespace CivicLens.Tests.Fixtures;

internal static class BillTextFixtures
{
    public const string SourceId = "congress-bill-text";
    public const string SourceUrl = "https://api.govinfo.gov/packages";
    public const string PackageId = "BILLS-119hr7ih";
    public const string PublicLawId = "PLAW-119publ21";
    public const string PublicLawUrl = "https://www.congress.gov/119/plaws/publ21/PLAW-119publ21.htm";
    public const string VersionsUrl = "https://api.congress.gov/v3/bill/119/hr/7/text?format=json&limit=250";

    public static string PackageUrl(string packageId = PackageId) => BillTextUrl.Package(SourceUrl, packageId);

    public static string CongressUrl(string packageId = PackageId, string extension = "htm") =>
        $"https://www.congress.gov/119/bills/hr7/{packageId}.{extension}";

    /// <summary>A /text response entry in the Congress.gov shape; a null date is written as JSON null.</summary>
    public static string Version(string type, string? date, string packageId = PackageId, bool withFormattedText = true,
        string? url = null)
    {
        var formats = withFormattedText
            ? $$$"""{"type":"Formatted Text","url":"{{{url ?? CongressUrl(packageId)}}}"},"""
            : "";
        var dateJson = date is null ? "null" : $"\"{date}\"";
        return $$$"""{"date":{{{dateJson}}},"formats":[{{{formats}}}{"type":"PDF","url":"{{{CongressUrl(packageId, "pdf")}}}"}],"type":"{{{type}}}"}""";
    }

    public static string Versions(params string[] versions) =>
        $$$"""{"textVersions":[{{{string.Join(',', versions)}}}],"pagination":{"count":{{{versions.Length}}}}}""";

    public static WatchedSourceConfiguration Source(bool enabled = true) => new()
    {
        Id = SourceId,
        Url = SourceUrl,
        AllowedOrigin = "https://api.govinfo.gov",
        AllowedPathPrefix = "/packages",
        Mode = CollectionMode.Api,
        Credential = CollectionCredentials.CongressGov,
        ApiParser = ApiParser.CongressBillText,
        Enabled = enabled,
        MaxRequests = 2,
        MaxBytes = 20_000_000,
        TimeoutSeconds = 60,
        AdmissionPolicy = new DiscoveryAdmissionPolicy { MaxJobs = 1, MaxTotalRequests = 12, MaxTotalBytes = 120_000_000, MaxTotalTimeoutSeconds = 720 }
    };

    public static CollectionConfiguration Configuration(bool enabled = true)
    {
        var bills = CongressBillSourceConfigurationTests.Configuration();
        return bills with { Sources = [.. bills.Sources, Source(enabled)] };
    }
}
