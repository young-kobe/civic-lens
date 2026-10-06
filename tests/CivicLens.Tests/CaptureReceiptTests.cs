using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using CivicLens.Infrastructure;

namespace CivicLens.Tests;

public sealed class CaptureReceiptTests
{
    [Fact]
    public async Task CaptureVerificationRejectsSubstitutionAndIncorrectLength()
    {
        var directory = Path.Combine(Path.GetTempPath(), "civic-receipt-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var bytes = Encoding.UTF8.GetBytes("A source passage with exact bytes.\n");
            var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
            var request = CollectionProtocolTests.Request() with { ArtifactDirectory = directory };
            var result = CollectionProtocolTests.Receipt(request) with
            {
                Sha256 = hash,
                ArtifactPath = hash + ".gz",
                ArtifactBytes = bytes.Length,
                BytesReceived = bytes.Length + 10
            };
            var path = Path.Combine(directory, result.ArtifactPath);
            await WriteAsync(path, bytes);
            await CollectorProcess.VerifyCaptureAsync(request, result);
            await Assert.ThrowsAsync<InvalidDataException>(() => CollectorProcess.VerifyCaptureAsync(request, result with { ArtifactBytes = bytes.Length - 1 }));
            await Assert.ThrowsAsync<InvalidDataException>(() => CollectorProcess.VerifyCaptureAsync(request, result with { ArtifactBytes = bytes.Length + 1 }));
            await WriteAsync(path, Encoding.UTF8.GetBytes("Changed"));
            await Assert.ThrowsAsync<InvalidDataException>(() => CollectorProcess.VerifyCaptureAsync(request, result));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static async Task WriteAsync(string path, byte[] bytes)
    {
        await using var file = File.Create(path);
        await using var gzip = new GZipStream(file, CompressionMode.Compress);
        await gzip.WriteAsync(bytes);
    }
}
