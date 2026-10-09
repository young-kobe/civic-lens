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

    public static async Task<PreviousRelease> LoadAsync(IReleaseDirectory releases, PublicationReleaseSummary? active,
        CancellationToken cancellationToken)
    {
        if (active is null) return new(null, []);
        var bytes = await releases.ReadFileAsync(active.DirectoryName, PublicationProtocol.ManifestPath,
            PublicationProtocol.MaximumRecordFileBytes, cancellationToken);
        var manifest = JsonSerializer.Deserialize<PublicationRelease>(bytes, PublicationProtocol.JsonOptions)
            ?? throw new InvalidDataException("Release manifest is empty.");
        manifest.Validate();
        if (manifest.ReleaseNumber != active.ReleaseNumber)
            throw new InvalidDataException("Release manifest does not match the active release.");
        return new(active.DirectoryName, manifest.Records);
    }

    public PublishedRecordEntry? Find(string recordId) =>
        Entries.FirstOrDefault(entry => entry.RecordId == recordId);
}
