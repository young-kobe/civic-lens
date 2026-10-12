using System.IO.Compression;
using System.Security.Cryptography;
using CivicLens.Core.Collection;
using CivicLens.Infrastructure.Documents;

namespace CivicLens.Tests.Fixtures;

internal sealed class TestCapture : IAsyncDisposable
{
    private TestCapture(string root, byte[] entity, string contentType, IReadOnlyList<string> encodings)
    {
        Root = root;
        Path = System.IO.Path.Combine(root, Convert.ToHexStringLower(SHA256.HashData(entity)) + ".gz");
        Attempt = new CapturedAttemptResult("attempt", "source", "https://example.test/a", "https://example.test/a",
            DateTimeOffset.UnixEpoch.AddSeconds(1),
            new CollectionResponse(200, null, null, contentType, encodings),
            new CaptureIdentity(Convert.ToHexStringLower(SHA256.HashData(entity)), entity.Length));
    }

    public string Root { get; }
    public string Path { get; }
    public CapturedAttemptResult Attempt { get; }
    public CaptureDocumentTextExtractor Extractor { get; } = new();

    public static async Task<TestCapture> CreateAsync(byte[] entity, string contentType,
        IReadOnlyList<string>? encodings = null)
    {
        var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "civic-lens-extraction-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var capture = new TestCapture(root, entity, contentType, encodings ?? []);
        await using var file = File.Create(capture.Path);
        await using var gzip = new GZipStream(file, CompressionLevel.SmallestSize);
        await gzip.WriteAsync(entity);
        return capture;
    }

    public ValueTask DisposeAsync()
    {
        Directory.Delete(Root, recursive: true);
        return ValueTask.CompletedTask;
    }
}
