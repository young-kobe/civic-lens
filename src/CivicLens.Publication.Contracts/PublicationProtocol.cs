using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CivicLens.Publication.Contracts;

public static class PublicationProtocol
{
    public const int SchemaVersion = 5;
    public const int OldestReadableSchemaVersion = 5;
    public const string ManifestPath = "release.json";
    public const string ManifestGzipPath = ManifestPath + ".gz";
    public const string ManifestBrotliPath = ManifestPath + ".br";
    public const int MaximumRecordsPerRelease = 20000;
    public const int PageSize = 50;
    public const int HomeRecordCount = 20;
    public const int MaximumFacetIdLength = 60;
    public const string HomePath = "index.html";
    public const string FacetsPath = "issues.html";
    public const int MaximumManifestBytes = 32 * 1024 * 1024;
    public const int SearchSourceTextCharacters = 200_000;
    public const string SearchPagePath = "search.html";
    public const string OfficialsPath = "officials.json";
    public const string CatalogPath = "catalog.json";
    public const string OfficialsPagePath = "officials.html";

    public static string AllRecordsPagePath(int page) => page == 1 ? "all.html" : $"all-{page}.html";
    public static string IssuePagePath(string issueId, int page) => FacetPagePath("issues", issueId, page);
    public static string PolicyAreaPagePath(string policyAreaId, int page) => FacetPagePath("policy-areas", policyAreaId, page);
    public static int PageCount(int recordCount) => Math.Max(1, (recordCount + PageSize - 1) / PageSize);
    public static bool IsPathSegment(string id) =>
        id.Length is > 0 and <= MaximumFacetIdLength && id.All(c => c is (>= '0' and <= '9') or (>= 'a' and <= 'z') or '-');
    public static string OfficialPagePath(string officialId) => $"officials/{officialId}.html";
    public static string OfficialVotesPath(string officialId, int congress) => $"officials/{officialId}/votes-{congress}.json";
    public static string OfficialVotesFolder(string officialId) => $"officials/{officialId}";
    public static string RecordDataPath(string recordId) => $"records/{recordId}.json";
    public static string RecordPagePath(string recordId) => $"records/{recordId}.html";

    private static string FacetPagePath(string folder, string facetId, int page) =>
        page == 1 ? $"{folder}/{facetId}.html" : $"{folder}/{facetId}.page-{page}.html";

    static PublicationProtocol() => JsonOptions.MakeReadOnly(populateMissingResolver: true);
    public static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web)
    {
        AllowDuplicateProperties = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        NumberHandling = JsonNumberHandling.Strict,
        RespectNullableAnnotations = true,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    internal static bool IsRecordId(PublishedRecordKind kind, string? value) => kind switch
    {
        PublishedRecordKind.DocumentChange => IsLowerHex(value, 32),
        PublishedRecordKind.Statement or PublishedRecordKind.Action or PublishedRecordKind.Brief or PublishedRecordKind.Vote
            or PublishedRecordKind.Relationship => IsSha256(value),
        _ => false
    };

    internal static bool IsSha256(string? value) => IsLowerHex(value, 64);

    private static bool IsLowerHex(string? value, int length) =>
        value?.Length == length && value.All(c => c is (>= '0' and <= '9') or (>= 'a' and <= 'f'));
}
