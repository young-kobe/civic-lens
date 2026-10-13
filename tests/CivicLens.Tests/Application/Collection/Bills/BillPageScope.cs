using CivicLens.Collection.Contracts;

namespace CivicLens.Tests.Application.Collection.Bills;

internal static class BillPageScope
{
    public const string SourceUrl = "https://api.congress.gov/v3/bill";

    public static CollectionRequest For(string url) => new()
    {
        JobId = "job",
        SourceId = "congress-bills",
        Url = url,
        AllowedOrigin = "https://api.congress.gov",
        AllowedPathPrefix = "/v3/bill",
        ArtifactDirectory = Path.GetFullPath("captures"),
        Mode = CollectionMode.Api,
        Credential = CollectionCredentials.CongressGov
    };
}
