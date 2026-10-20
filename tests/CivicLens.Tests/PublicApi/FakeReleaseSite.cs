using System.Net;
using System.Text.Json;
using CivicLens.Publication.Contracts;

namespace CivicLens.Tests.PublicApi;

/// <summary>Serves the release files of a static site from memory. It answers a conditional manifest request with 304 when the ETag matches.</summary>
internal sealed class FakeReleaseSite : HttpMessageHandler
{
    private readonly Dictionary<string, byte[]> files = [];
    private readonly Dictionary<string, byte[]> removed = [];
    private string entityTag = "\"none\"";

    public Exception? FailNextManifestRequest { get; set; }
    public int ManifestRequests { get; private set; }
    public int NotModifiedReplies { get; private set; }
    public List<string> RecordRequests { get; } = [];
    public List<string> Requests { get; } = [];

    public void Publish(int number, params SampleRecord[] records)
    {
        var manifest = new PublicationRelease
        {
            SchemaVersion = PublicationProtocol.SchemaVersion,
            ReleaseNumber = number,
            PublishedAtUtc = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero).AddDays(number),
            Records = [.. records.Select(record => record.Entry)]
        };
        files.Clear();
        files[PublicationProtocol.ManifestPath] = JsonSerializer.SerializeToUtf8Bytes(manifest, PublicationProtocol.JsonOptions);
        files[PublicationProtocol.OfficialsPath] = JsonSerializer.SerializeToUtf8Bytes(
            new PublishedOfficials { Officials = [new() { Id = "mayor", Name = "Mayor Example" }] }, PublicationProtocol.JsonOptions);
        files[PublicationProtocol.CatalogPath] = JsonSerializer.SerializeToUtf8Bytes(
            new PublishedCatalog { Issues = [new() { Id = "housing", Name = "Housing costs" }], PolicyAreas = [] }, PublicationProtocol.JsonOptions);
        foreach (var record in records)
            files[PublicationProtocol.RecordDataPath(record.Entry.RecordId)] = JsonSerializer.SerializeToUtf8Bytes(record.Body, PublicationProtocol.JsonOptions);
        entityTag = $"\"release-{number}\"";
    }

    public void Remove(string recordId) =>
        removed[PublicationProtocol.RecordDataPath(recordId)] = files.Remove(PublicationProtocol.RecordDataPath(recordId), out var content) ? content : [];

    public void Restore(string recordId)
    {
        var path = PublicationProtocol.RecordDataPath(recordId);
        files[path] = removed[path];
    }

    public void Corrupt(string recordId, Func<string, string> change)
    {
        var path = PublicationProtocol.RecordDataPath(recordId);
        files[path] = System.Text.Encoding.UTF8.GetBytes(change(System.Text.Encoding.UTF8.GetString(files[path])));
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath.TrimStart('/');
        Requests.Add(path);
        if (path == PublicationProtocol.ManifestPath)
        {
            ManifestRequests++;
            if (FailNextManifestRequest is { } failure)
            {
                FailNextManifestRequest = null;
                throw failure;
            }
            if (request.Headers.IfNoneMatch.Any(tag => tag.Tag == entityTag))
            {
                NotModifiedReplies++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotModified));
            }
        }
        else if (path.StartsWith("records/", StringComparison.Ordinal))
            RecordRequests.Add(path);
        if (!files.TryGetValue(path, out var content)) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(content) };
        response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue(entityTag);
        return Task.FromResult(response);
    }
}
