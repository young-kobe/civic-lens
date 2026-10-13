using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CivicLens.Application.Configuration;

internal sealed class ConfigurationSnapshotRules<TConfiguration>(Action<TConfiguration> validate)
    where TConfiguration : class
{
    public string Snapshot(TConfiguration configuration)
    {
        validate(configuration);
        return JsonSerializer.Serialize(configuration, ConfigurationJson.SnapshotOptions);
    }

    public string ComputeId(string json) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json)));

    public TConfiguration Verify(string id, string json)
    {
        if (!string.Equals(id, ComputeId(json), StringComparison.Ordinal))
            throw new ArgumentException("Revision ID does not match its configuration JSON.", nameof(id));

        var configuration = Parse(json);
        var canonicalJson = JsonSerializer.Serialize(configuration, ConfigurationJson.SnapshotOptions);
        if (!string.Equals(json, canonicalJson, StringComparison.Ordinal))
            throw new ArgumentException("Configuration JSON must use the canonical snapshot representation.", nameof(json));
        return configuration;
    }

    public TConfiguration Parse(string json)
    {
        try
        {
            var configuration = JsonSerializer.Deserialize<TConfiguration>(json, ConfigurationJson.Options)
                ?? throw new JsonException("Configuration JSON is empty.");
            validate(configuration);
            return configuration;
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("Configuration JSON is malformed or unsupported.", nameof(json), exception);
        }
    }
}
