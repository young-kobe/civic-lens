using System.Text.Json;
using System.Text.Json.Serialization;
using CivicLens.Application.Collection;
using CivicLens.Collection.Contracts;

namespace CivicLens.Application.Configuration;

public static class ConfigurationJson
{
    public static JsonSerializerOptions Options { get; } = CreateOptions(ignoreNulls: false);

    internal static JsonSerializerOptions SnapshotOptions { get; } = CreateOptions(ignoreNulls: true);

    private static JsonSerializerOptions CreateOptions(bool ignoreNulls)
    {
        var options = new JsonSerializerOptions(CollectionProtocol.JsonOptions)
        {
            DefaultIgnoreCondition = ignoreNulls ? JsonIgnoreCondition.WhenWritingNull : JsonIgnoreCondition.Never
        };
        options.Converters.Add(new JsonStringEnumConverter<ApiParser>(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}
