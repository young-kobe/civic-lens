using System.Text.Json;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

internal sealed class PreviousRelease
{
    private readonly IReleaseDirectory releases;
    private readonly string? directoryName;

    private PreviousRelease(IReleaseDirectory releases, string? directoryName, IReadOnlyList<PublishedRecordEntry> entries)
    {
        this.releases = releases;
        this.directoryName = directoryName;
        Entries = entries;
    }

    public IReadOnlyList<PublishedRecordEntry> Entries { get; }

    public static async Task<PreviousRelease> LoadAsync(IReleaseDirectory releases, IPublicationStore publications,
        PublicationReleaseSummary? latest, CancellationToken cancellationToken)
    {
        if (latest is null) return new(releases, null, []);
        var directory = await releases.GetActiveAsync(cancellationToken) ?? latest.DirectoryName;
        var bytes = await releases.ReadFileAsync(directory, PublicationProtocol.ManifestPath,
            PublicationProtocol.MaximumRecordFileBytes, cancellationToken);
        var manifest = JsonSerializer.Deserialize<PublicationRelease>(bytes, PublicationProtocol.JsonOptions)
            ?? throw new InvalidDataException("Release manifest is empty.");
        manifest.Validate();
        var recorded = await publications.GetAsync(manifest.ReleaseNumber, cancellationToken);
        if (recorded?.DirectoryName != directory)
            throw new InvalidDataException("Release manifest does not match a recorded release.");
        return new(releases, directory, manifest.Records);
    }

    public PublishedRecordEntry? Find(string recordId) =>
        Entries.FirstOrDefault(entry => entry.RecordId == recordId);

    public async Task<(byte[] Json, PublishedDocumentChange Record)> ReadRecordAsync(PublishedRecordEntry entry,
        CancellationToken cancellationToken)
    {
        var json = await releases.ReadFileAsync(directoryName!, PublicationProtocol.RecordDataPath(entry.RecordId),
            PublicationProtocol.MaximumRecordFileBytes, cancellationToken);
        var record = JsonSerializer.Deserialize<PublishedDocumentChange>(json, PublicationProtocol.JsonOptions)
            ?? throw new InvalidDataException("Published record file is empty.");
        record.Validate();
        if (record.RecordId != entry.RecordId || record.RevisionNumber != entry.RevisionNumber)
            throw new InvalidDataException("Published record file does not match its manifest entry.");
        return (json, record);
    }
}
