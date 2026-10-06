using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using CivicLens.Collection.Contracts;
using CivicLens.Infrastructure.Collection;
using static CivicLens.Tests.Fixtures.CollectionFixtures;

namespace CivicLens.Tests.Infrastructure.Collection;

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
            var request = Request() with { ArtifactDirectory = directory };
            var result = Receipt(request) with
            {
                Capture = new CaptureArtifact { Sha256 = hash, RelativePath = hash + ".gz", ByteLength = bytes.Length },
                BytesReceived = bytes.Length + 10
            };
            var path = Path.Combine(directory, result.Capture!.RelativePath);
            await WriteAsync(path, bytes);
            await CollectorProcess.VerifyCaptureAsync(request, result);
            await Assert.ThrowsAsync<InvalidDataException>(() => CollectorProcess.VerifyCaptureAsync(request, result with { Capture = result.Capture! with { ByteLength = bytes.Length - 1 } }));
            await Assert.ThrowsAsync<InvalidDataException>(() => CollectorProcess.VerifyCaptureAsync(request, result with { Capture = result.Capture! with { ByteLength = bytes.Length + 1 } }));
            await WriteAsync(path, Encoding.UTF8.GetBytes("Changed"));
            await Assert.ThrowsAsync<InvalidDataException>(() => CollectorProcess.VerifyCaptureAsync(request, result));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task ReplayVerifierUsesRelocatedRootAndDoesNotRequireArtifactForFailureReceipt()
    {
        var originalRoot = Path.Combine(Path.GetTempPath(), "original-captures-" + Guid.NewGuid().ToString("N"));
        var relocatedRoot = Path.Combine(Path.GetTempPath(), "relocated-captures-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(relocatedRoot);
        try
        {
            var request = Request() with { ArtifactDirectory = originalRoot };
            var failed = Receipt(request) with
            {
                Outcome = CollectionOutcome.Failed,
                FailureCode = CollectionFailureCode.TransportError,
                Response = null,
                Capture = null,
                BytesReceived = 0,
                RequestCount = 1
            };

            await new CaptureArtifactVerifier().VerifyAsync(request, failed, relocatedRoot, CancellationToken.None);
            Assert.False(Directory.Exists(originalRoot));
        }
        finally { Directory.Delete(relocatedRoot, recursive: true); }
    }

    [Fact]
    public async Task RelocatedCaptureIsRehashedAndCorruptionIsRejected()
    {
        var originalRoot = Path.Combine(Path.GetTempPath(), "old-captures-" + Guid.NewGuid().ToString("N"));
        var relocatedRoot = Path.Combine(Path.GetTempPath(), "new-captures-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(relocatedRoot);
        try
        {
            var bytes = Encoding.UTF8.GetBytes("Relocated exact bytes.");
            var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
            var request = Request() with { ArtifactDirectory = originalRoot };
            var receipt = Receipt(request) with
            {
                Capture = new CaptureArtifact { Sha256 = hash, RelativePath = hash + ".gz", ByteLength = bytes.Length },
                BytesReceived = bytes.Length + 10
            };
            var artifact = Path.Combine(relocatedRoot, receipt.Capture!.RelativePath);
            await WriteAsync(artifact, bytes);
            var verifier = new CaptureArtifactVerifier();

            await verifier.VerifyAsync(request, receipt, relocatedRoot, CancellationToken.None);
            await WriteAsync(artifact, Encoding.UTF8.GetBytes("tampered"));
            await Assert.ThrowsAsync<InvalidDataException>(() => verifier.VerifyAsync(request, receipt,
                relocatedRoot, CancellationToken.None));
            Assert.False(Directory.Exists(originalRoot));
        }
        finally { Directory.Delete(relocatedRoot, recursive: true); }
    }

    private static async Task WriteAsync(string path, byte[] bytes)
    {
        await using var file = File.Create(path);
        await using var gzip = new GZipStream(file, CompressionMode.Compress);
        await gzip.WriteAsync(bytes);
    }
}
