using System.Text.Json;
using System.Text.Json.Serialization;

namespace CivicLens.Collection.Contracts;

public static class CollectionProtocol
{
    public const int Version = 5;
    public const int PreviousVersion = 4;
    public const int LegacyPageVersion = 3;
    // Includes JSON escaping for up to 1,000 URLs of 4,096 characters plus response metadata.
    public const int MaximumDiscoveryReceiptSize = 32 * 1024 * 1024;

    static CollectionProtocol() => JsonOptions.MakeReadOnly(populateMissingResolver: true);
    public static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web)
    {
        AllowDuplicateProperties = false,
        NumberHandling = JsonNumberHandling.Strict,
        RespectNullableAnnotations = true,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters =
        {
            new JsonStringEnumConverter<CollectionOutcome>(JsonNamingPolicy.CamelCase, allowIntegerValues: false),
            new JsonStringEnumConverter<CollectionFailureCode>(JsonNamingPolicy.CamelCase, allowIntegerValues: false),
            new JsonStringEnumConverter<CollectionMode>(JsonNamingPolicy.CamelCase, allowIntegerValues: false),
            new JsonStringEnumConverter<DiscoveryStatus>(JsonNamingPolicy.CamelCase, allowIntegerValues: false)
        }
    };
}
