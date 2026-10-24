using CivicLens.Collection.Contracts;

namespace CivicLens.Application.Collection;

/// <summary>A verified collector receipt saved before its evidence import is attempted.</summary>
public sealed record PendingCollectionHandoff(int Version, string AttemptId, CollectionRequest Request,
    CollectionResult Receipt)
{
    public const int CurrentVersion = 1;

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

    public static bool IsValidAttemptId(string? value) => value is { Length: > 0 and <= 128 } &&
        value.All(c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '-' or '_');
}
