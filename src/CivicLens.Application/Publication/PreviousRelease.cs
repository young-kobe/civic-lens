using System.Text.Json;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

internal sealed class PreviousRelease
{
    private PreviousRelease(string? directoryName, IReadOnlyList<PublishedRecordEntry> entries)
    {
        DirectoryName = directoryName;
        Entries = entries;
    }

    public string? DirectoryName { get; }

    public IReadOnlyList<PublishedRecordEntry> Entries { get; }

    public static async Task<PreviousRelease> LoadAsync(IReleaseDirectory releases, IPublicationStore publications,
        PublicationReleaseSummary? latest, CancellationToken cancellationToken)
    {
        if (latest is null) return new(null, []);
        var directory = await releases.GetActiveAsync(cancellationToken) ?? latest.DirectoryName;
        var bytes = await releases.ReadFileAsync(directory, PublicationProtocol.ManifestPath,
            PublicationProtocol.MaximumRecordFileBytes, cancellationToken);
        var manifest = JsonSerializer.Deserialize<PublicationRelease>(bytes, PublicationProtocol.JsonOptions)
            ?? throw new InvalidDataException("Release manifest is empty.");
        manifest.Validate();
        var recorded = await publications.GetAsync(manifest.ReleaseNumber, cancellationToken);
        if (recorded?.DirectoryName != directory)
            throw new InvalidDataException("Release manifest does not match a recorded release.");
        return new(directory, manifest.Records);
    }

    public PublishedRecordEntry? Find(string recordId) =>
        Entries.FirstOrDefault(entry => entry.RecordId == recordId);
}
