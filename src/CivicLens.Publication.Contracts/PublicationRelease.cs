namespace CivicLens.Publication.Contracts;

public sealed record PublicationRelease
{
    public required int SchemaVersion { get; init; }
    public required int ReleaseNumber { get; init; }
    public required DateTimeOffset PublishedAtUtc { get; init; }
    public required PublishedRecordEntry[] Records { get; init; }

    public void Validate()
    {
        if (SchemaVersion != PublicationProtocol.SchemaVersion || ReleaseNumber < 1 || Records is null ||
            Records.Length > PublicationProtocol.MaximumRecordsPerRelease)
            throw new InvalidDataException("Release manifest is invalid.");
        foreach (var entry in Records) entry?.Validate();
        if (Records.Any(entry => entry is null) ||
            Records.Select(entry => entry.RecordId).Distinct(StringComparer.Ordinal).Count() != Records.Length)
            throw new InvalidDataException("Release manifest records must be present and unique.");
    }
}
