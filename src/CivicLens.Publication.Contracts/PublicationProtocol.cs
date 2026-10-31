using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CivicLens.Publication.Contracts;

public static class PublicationProtocol
{
    public const int SchemaVersion = 1;
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
    public const string ReportPagePath = "report.html";
    public const string MethodPagePath = "method.html";
    public const string OfficialsPath = "officials.json";
    public const string OfficialSummariesPath = "officials-summary.json";
    public const string BillStatusPath = "bill-status.json";
    public const string CatalogPath = "catalog.json";
    public const string OfficialsPagePath = "officials.html";
    public const string AboutPagePath = "about.html";
    public const string GlossaryPagePath = "glossary.html";
    public const string SitemapPath = "sitemap.xml";
    public const string RobotsPath = "robots.txt";
    public const string NotFoundPagePath = "404.html";
    public const int MaximumOfficialFiles = 512;
    private const string PhotoFolder = "photos/";
    private const string PhotoExtension = ".jpg";

    public static string KindText(PublishedRecordKind kind) => JsonSerializer.Serialize(kind, JsonOptions).Trim('"');
    public static string SearchByKindPath(PublishedRecordKind kind) => $"{SearchPagePath}?kind={KindText(kind)}";
    public static string AllRecordsPagePath(int page) => page == 1 ? "all.html" : $"all-{page}.html";
    public static string BriefsPagePath(int page) => page == 1 ? "briefs.html" : $"briefs-{page}.html";
    public static string IssuePagePath(string issueId, int page) => FacetPagePath("issues", issueId, page);
    public static string PolicyAreaPagePath(string policyAreaId, int page) => FacetPagePath("policy-areas", policyAreaId, page);
    public static int PageCount(int recordCount) => Math.Max(1, (recordCount + PageSize - 1) / PageSize);
    public static bool IsPathSegment(string id) =>
        id.Length is > 0 and <= MaximumFacetIdLength && id.All(c => c is (>= '0' and <= '9') or (>= 'a' and <= 'z') or '-');
    public static string OfficialPagePath(string officialId) => $"officials/{officialId}.html";
    public static string OfficialCongressPagePath(string officialId, int congress) => $"officials/{officialId}/{congress}.html";
    public static string OfficialVotesPath(string officialId, int congress) => $"officials/{officialId}/votes-{congress}.json";
    public static string OfficialFolder(string officialId) => $"officials/{officialId}";
    public static string PhotoPath(string captureSha256) => $"{PhotoFolder}{captureSha256}{PhotoExtension}";
    public static bool IsPhotoPath(string? path) =>
        path is not null && path.StartsWith(PhotoFolder, StringComparison.Ordinal) && path.EndsWith(PhotoExtension, StringComparison.Ordinal) &&
        IsSha256(path[PhotoFolder.Length..^PhotoExtension.Length]);
    public static int? VotesCongressOf(string path) => CongressIn(path, "/votes-", ".json");
    public static int? CongressPageOf(string path) => CongressIn(path, "/", ".html");
    public static string RecordDataPath(string recordId) => $"records/{recordId}.json";
    public static string RecordPagePath(string recordId) => $"records/{recordId}.html";
    public static string ReportPageLink(string recordId, int revision) =>
        $"{ReportPagePath}?{ReaderReportProtocol.RecordIdField}={recordId}&{ReaderReportProtocol.RevisionField}={revision}";

    private static int? CongressIn(string path, string prefix, string suffix)
    {
        var name = path[path.LastIndexOf('/')..];
        if (!name.StartsWith(prefix, StringComparison.Ordinal) || !name.EndsWith(suffix, StringComparison.Ordinal)) return null;
        var digits = name[prefix.Length..^suffix.Length];
        return digits.Length is > 0 and <= 4 && digits[0] != '0' && digits.All(char.IsAsciiDigit) ? int.Parse(digits) : null;
    }

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
            or PublishedRecordKind.Relationship or PublishedRecordKind.ExecutiveAction => IsSha256(value),
        _ => false
    };

    /// <summary>A record ID of any kind: 32 or 64 lower hex characters.</summary>
    public static bool IsAnyRecordId(string? value) => IsLowerHex(value, 32) || IsLowerHex(value, 64);

    internal static bool IsSha256(string? value) => IsLowerHex(value, 64);

    private static bool IsLowerHex(string? value, int length) =>
        value?.Length == length && value.All(c => c is (>= '0' and <= '9') or (>= 'a' and <= 'f'));
}
