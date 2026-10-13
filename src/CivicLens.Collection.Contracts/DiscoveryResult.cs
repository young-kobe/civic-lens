using System.Text.Json.Serialization;

namespace CivicLens.Collection.Contracts;

public enum DiscoveryStatus
{
    Parsed,
    Invalid,
    Unsupported,
    LimitExceeded
}

public sealed record DiscoveryResult
{
    public required DiscoveryStatus Status { get; init; }
    public required string[] Urls { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? OutOfScopeCount { get; init; }

    public void ValidateAgainst(CollectionRequest request)
    {
        if (request is null)
            throw new InvalidDataException("Collection request is required for discovery validation.");
        request.ValidateReadable();
        if (Urls is null || !Enum.IsDefined(Status))
            throw new InvalidDataException("Discovery metadata is invalid.");
        if (Status != DiscoveryStatus.Parsed && Urls.Length != 0)
            throw new InvalidDataException("Failed discovery cannot contain candidates.");
        if (!HasValidOutOfScopeCount(request.Version))
            throw new InvalidDataException("Out-of-scope count is invalid for this protocol version.");
        if (Urls.Length > request.MaxCandidates)
            throw new InvalidDataException("Discovery exceeds the candidate limit.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in Urls)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 4096 ||
                !Uri.TryCreate(value, UriKind.Absolute, out var url) || !request.Allows(url) || !seen.Add(value))
                throw new InvalidDataException("Discovery candidate is invalid, duplicated, or outside request scope.");
        }
    }

    private bool HasValidOutOfScopeCount(int version)
    {
        if (version < CollectionProtocol.SchemeUpgradeVersion) return OutOfScopeCount is null;
        if (Status != DiscoveryStatus.Parsed) return OutOfScopeCount == 0;
        return OutOfScopeCount >= 0;
    }
}
