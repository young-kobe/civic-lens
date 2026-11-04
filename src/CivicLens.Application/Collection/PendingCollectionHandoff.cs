using System.Text.Json.Nodes;
using CivicLens.Collection.Contracts;

namespace CivicLens.Application.Collection;

/// <summary>A verified collector receipt saved before its evidence import is attempted.</summary>
public sealed record PendingCollectionHandoff(int Version, string AttemptId, CollectionRequest Request,
    CollectionResult Receipt)
{
    public const int CurrentVersion = 2;

    public void Validate()
    {
        if (Version != CurrentVersion)
            throw new InvalidDataException($"Receipt handoff version {Version} is unsupported.");
        if (!IsValidAttemptId(AttemptId))
            throw new InvalidDataException("Receipt handoff attempt ID is invalid.");
        ArgumentNullException.ThrowIfNull(Request);
        ArgumentNullException.ThrowIfNull(Receipt);
        Request.Validate();
        Receipt.ValidateAgainst(Request);
    }

    public static void ThrowIfRefused(JsonNode envelope)
    {
        if (envelope["version"] is JsonValue version && version.TryGetValue<int>(out var number) && number != CurrentVersion)
            throw new RefusedCollectionHandoffException($"Receipt handoff version {number} is unsupported.");
        if (envelope["receipt"] is JsonObject receipt && !receipt.ContainsKey("elapsedMilliseconds"))
            throw new RefusedCollectionHandoffException("Receipt handoff has no elapsed time.");
    }

    public static bool IsValidAttemptId(string? value) => value is { Length: > 0 and <= 128 } &&
        value.All(c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '-' or '_');
}
