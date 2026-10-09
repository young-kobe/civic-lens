using System.Text.Json;
using System.Text.Json.Serialization;

namespace CivicLens.Publication.Contracts;

public static class PublicationProtocol
{
    public const int SchemaVersion = 1;
    public const string ManifestPath = "release.json";
    public const int MaximumRecordsPerRelease = 2000;
    public const int IndexPageSize = 50;
    public const int MaximumRecordFileBytes = 16 * 1024 * 1024;

    public static string IndexPagePath(int page) => page == 1 ? "index.html" : $"page-{page}.html";
    public static int IndexPageCount(int recordCount) => Math.Max(1, (recordCount + IndexPageSize - 1) / IndexPageSize);
    public static string RecordDataPath(string recordId) => $"records/{recordId}.json";
    public static string RecordPagePath(string recordId) => $"records/{recordId}.html";

    static PublicationProtocol() => JsonOptions.MakeReadOnly(populateMissingResolver: true);
    public static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web)
    {
        AllowDuplicateProperties = false,
        NumberHandling = JsonNumberHandling.Strict,
        RespectNullableAnnotations = true,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    internal static bool IsRecordId(string? value) => IsLowerHex(value, 32);
    internal static bool IsSha256(string? value) => IsLowerHex(value, 64);

    private static bool IsLowerHex(string? value, int length) =>
        value?.Length == length && value.All(c => c is (>= '0' and <= '9') or (>= 'a' and <= 'f'));
}
