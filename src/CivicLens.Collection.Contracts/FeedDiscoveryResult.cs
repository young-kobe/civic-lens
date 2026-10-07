namespace CivicLens.Collection.Contracts;

public enum FeedDiscoveryStatus
{
    Parsed,
    Invalid,
    Unsupported,
    LimitExceeded
}

public sealed record FeedDiscoveryResult
{
    public required FeedDiscoveryStatus Status { get; init; }
    public required string[] Urls { get; init; }

    public void ValidateAgainst(CollectionRequest request)
    {
        if (request is null)
            throw new InvalidDataException("Collection request is required for feed discovery validation.");
        request.Validate();
        if (Urls is null || !Enum.IsDefined(Status))
            throw new InvalidDataException("Feed discovery metadata is invalid.");
        if (Status != FeedDiscoveryStatus.Parsed && Urls.Length != 0)
            throw new InvalidDataException("Failed feed discovery cannot contain candidates.");
        if (Urls.Length > request.MaxCandidates)
            throw new InvalidDataException("Feed discovery exceeds the candidate limit.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in Urls)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 4096 ||
                !Uri.TryCreate(value, UriKind.Absolute, out var url) || !request.Allows(url) || !seen.Add(value))
                throw new InvalidDataException("Feed candidate is invalid, duplicated, or outside request scope.");
        }
    }
}
