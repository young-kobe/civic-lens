using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CivicLens.Collection.Contracts;

namespace CivicLens.Application.Collection;

public sealed record CollectionConfigurationRevision
{
    private static readonly JsonSerializerOptions SnapshotJsonOptions = CreateSnapshotJsonOptions();

    [JsonConstructor]
    public CollectionConfigurationRevision(string id, string json)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(json);

        var expectedId = ComputeId(json);
        if (!string.Equals(id, expectedId, StringComparison.Ordinal))
            throw new ArgumentException("Revision ID does not match its configuration JSON.", nameof(id));

        var configuration = ParseConfiguration(json);

        var canonicalJson = JsonSerializer.Serialize(configuration, SnapshotJsonOptions);
        if (!string.Equals(json, canonicalJson, StringComparison.Ordinal))
            throw new ArgumentException("Configuration JSON must use the canonical snapshot representation.", nameof(json));

        Id = id;
        Json = json;
    }

    public string Id { get; }

    public string Json { get; }

    public CollectionConfiguration ReadConfiguration() => ParseConfiguration(Json);

    public static CollectionConfigurationRevision Create(CollectionConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        configuration.Validate();

        var json = JsonSerializer.Serialize(configuration, SnapshotJsonOptions);
        return new CollectionConfigurationRevision(ComputeId(json), json);
    }

    private static string ComputeId(string json) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json)));

    private static CollectionConfiguration ParseConfiguration(string json)
    {
        try
        {
            var configuration = JsonSerializer.Deserialize<CollectionConfiguration>(json, CollectionProtocol.JsonOptions)
                ?? throw new JsonException("Configuration JSON is empty.");
            configuration.Validate();
            return configuration;
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("Configuration JSON is malformed or unsupported.", nameof(json), exception);
        }
    }

    private static JsonSerializerOptions CreateSnapshotJsonOptions()
    {
        var options = new JsonSerializerOptions(CollectionProtocol.JsonOptions)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}
