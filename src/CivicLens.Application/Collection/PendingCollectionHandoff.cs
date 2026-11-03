using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using CivicLens.Collection.Contracts;

namespace CivicLens.Application.Collection;

/// <summary>A verified collector receipt saved before its evidence import is attempted.</summary>
public sealed record PendingCollectionHandoff(int Version, string AttemptId, CollectionRequest Request,
    CollectionResult Receipt)
{
    public const int ElapsedUnknownVersion = 1;
    public const int CurrentVersion = 2;

    [JsonIgnore]
    public long? ElapsedMilliseconds => Version == ElapsedUnknownVersion ? null : Receipt.ElapsedMilliseconds;

    public static PendingCollectionHandoff? Read(ReadOnlySpan<byte> json)
    {
        var envelope = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { AllowDuplicateProperties = false });
        if (envelope?["version"] is JsonValue version && version.TryGetValue<int>(out var number) &&
            number == ElapsedUnknownVersion && envelope["receipt"] is JsonObject receipt)
            receipt["elapsedMilliseconds"] = 0;
        return envelope.Deserialize<PendingCollectionHandoff>(CollectionProtocol.JsonOptions);
    }

    public void Validate()
    {
        if (Version is not (ElapsedUnknownVersion or CurrentVersion))
            throw new InvalidDataException($"Receipt handoff version {Version} is unsupported.");
        if (!IsValidAttemptId(AttemptId))
            throw new InvalidDataException("Receipt handoff attempt ID is invalid.");
        ArgumentNullException.ThrowIfNull(Request);
        ArgumentNullException.ThrowIfNull(Receipt);
        Request.Validate();
        Receipt.ValidateAgainst(Request);
    }

    public static bool IsValidAttemptId(string? value) => value is { Length: > 0 and <= 128 } &&
        value.All(c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '-' or '_');
}
