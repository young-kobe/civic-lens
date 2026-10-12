using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CivicLens.Collection.Contracts;

namespace CivicLens.Application.Configuration;

internal sealed class ConfigurationSnapshotRules<TConfiguration>(Action<TConfiguration> validate)
    where TConfiguration : class
{
    private static readonly JsonSerializerOptions SnapshotJsonOptions = CreateSnapshotJsonOptions();

    public string Snapshot(TConfiguration configuration)
    {
        validate(configuration);
        return JsonSerializer.Serialize(configuration, SnapshotJsonOptions);
    }

    public string ComputeId(string json) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json)));

    public void Verify(string id, string json)
    {
        if (!string.Equals(id, ComputeId(json), StringComparison.Ordinal))
            throw new ArgumentException("Revision ID does not match its configuration JSON.", nameof(id));

        var canonicalJson = JsonSerializer.Serialize(Parse(json), SnapshotJsonOptions);
        if (!string.Equals(json, canonicalJson, StringComparison.Ordinal))
            throw new ArgumentException("Configuration JSON must use the canonical snapshot representation.", nameof(json));
    }

    public TConfiguration Parse(string json)
    {
        try
        {
            var configuration = JsonSerializer.Deserialize<TConfiguration>(json, CollectionProtocol.JsonOptions)
                ?? throw new JsonException("Configuration JSON is empty.");
            validate(configuration);
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
