using System.Text.Json;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

/// <summary>Reads the file of one record of the new release: from this publish, or from the previous release.</summary>
internal static class ReleaseRecordFiles
{
    public static async Task<TRecord> ReadAsync<TRecord>(IReleaseDirectory releases, PublishedRecordEntry entry,
        IReadOnlyList<PendingRecord> additions, PreviousRelease previous, CancellationToken cancellationToken)
        where TRecord : class
    {
        var json = additions.FirstOrDefault(addition => addition.Entry.Kind == entry.Kind && addition.RecordId == entry.RecordId)?.Json
            ?? await ReadAsync(releases, previous.DirectoryName!, entry.RecordId, cancellationToken);
        return Deserialize<TRecord>(json);
    }

    public static Task<byte[]> ReadAsync(IReleaseDirectory releases, string directoryName, string recordId, CancellationToken cancellationToken) =>
        releases.ReadFileAsync(directoryName, PublicationProtocol.RecordDataPath(recordId), PublishedRecordFiles.MaximumCitedRecordBytes,
            cancellationToken);

    public static TRecord Deserialize<TRecord>(byte[] json) where TRecord : class =>
        JsonSerializer.Deserialize<TRecord>(json, PublicationProtocol.JsonOptions) ?? throw new InvalidDataException("A record file is empty.");
}
