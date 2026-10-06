namespace CivicLens.Collection.Contracts;

/// <summary>Identifies the gzip storage envelope; hash and length describe bytes after removing that envelope, while retaining any HTTP Content-Encoding.</summary>
public sealed record CaptureArtifact
{
    public required string Sha256 { get; init; }
    public required string RelativePath { get; init; }
    public required long ByteLength { get; init; }

    public void Validate()
    {
        if (Sha256 is not { Length: 64 } hash || hash.Any(c => c is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')) ||
            RelativePath != hash + ".gz" || ByteLength < 0)
            throw new InvalidDataException("Capture artifact metadata is invalid.");
    }
}
