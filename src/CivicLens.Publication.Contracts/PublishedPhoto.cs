namespace CivicLens.Publication.Contracts;

/// <summary>The portrait of an official. The path names a file in the release, and its name is the SHA-256 hash of the capture, not of the decoded image bytes.</summary>
public sealed record PublishedPhoto
{
    public const int MaximumDimension = 1000;

    public required string Path { get; init; }
    public required string Credit { get; init; }
    public required string SourceUrl { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }

    public void Validate()
    {
        if (!PublicationProtocol.IsPhotoPath(Path) || !PublishedEvidenceRules.IsBoundedText(Credit) || !PublishedWebsite.IsValid(SourceUrl) ||
            Width is < 1 or > MaximumDimension || Height is < 1 or > MaximumDimension)
            throw new InvalidDataException("Published photo is invalid.");
    }
}
