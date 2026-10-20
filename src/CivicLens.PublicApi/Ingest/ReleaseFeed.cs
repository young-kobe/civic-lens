using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using CivicLens.Publication.Contracts;

namespace CivicLens.PublicApi.Ingest;

/// <summary>Reads the public release files over HTTP. The manifest read is conditional, so an unchanged release costs one empty reply.</summary>
public sealed class ReleaseFeed(HttpClient http)
{
    // Large enough for the biggest record file that the publisher can write.
    public const int MaximumRecordBytes = 128 * 1024 * 1024;

    private EntityTagHeaderValue? entityTag;
    private DateTimeOffset? lastModified;
    private EntityTagHeaderValue? readTag;
    private DateTimeOffset? readModified;

    public async Task<PublicationRelease?> ReadChangedManifestAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, PublicationProtocol.ManifestPath);
        request.Headers.IfNoneMatch.Clear();
        if (entityTag is not null) request.Headers.IfNoneMatch.Add(entityTag);
        request.Headers.IfModifiedSince = lastModified;
        using var response = await http.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotModified) return null;
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        if (json.Length > PublicationProtocol.MaximumManifestBytes)
            throw new InvalidDataException("The release manifest is over the manifest size limit.");
        var release = JsonSerializer.Deserialize<PublicationRelease>(json, PublicationProtocol.JsonOptions)
            ?? throw new InvalidDataException("The release manifest is empty.");
        release.Validate();
        readTag = response.Headers.ETag;
        readModified = response.Content.Headers.LastModified;
        return release;
    }

    /// <summary>Keeps the validators of the last manifest read. Call it after the release is stored, so a failed ingest reads the manifest again.</summary>
    public void KeepValidators()
    {
        entityTag = readTag;
        lastModified = readModified;
    }

    public async Task<byte[]> ReadRecordAsync(string recordId, CancellationToken cancellationToken) =>
        await http.GetByteArrayAsync(PublicationProtocol.RecordDataPath(recordId), cancellationToken);
}
