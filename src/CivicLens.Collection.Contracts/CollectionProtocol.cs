using System.Text.Json;
using System.Text.Json.Serialization;

namespace CivicLens.Collection.Contracts;

public static class CollectionProtocol
{
    public const int Version = 2;

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
            new JsonStringEnumConverter<CollectionFailureCode>(JsonNamingPolicy.CamelCase, allowIntegerValues: false)
        }
    };
}
