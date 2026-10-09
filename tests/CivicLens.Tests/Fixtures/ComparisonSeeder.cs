using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using CivicLens.Application.Collection;
using CivicLens.Collection.Contracts;
using CivicLens.Core.Collection;
using CivicLens.Core.Documents;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Documents;
using static CivicLens.Tests.Fixtures.CollectionFixtures;

namespace CivicLens.Tests.Fixtures;

internal sealed class ComparisonSeeder(PostgresCollectionAttemptStore attempts, PostgresDocumentExtractionStore extractions,
    PostgresDocumentComparisonStore comparisons, string captureRoot)
{
    public async Task<DocumentComparison> SaveAsync(string beforeText, string afterText)
    {
        var before = new DocumentExtraction(await ImportAsync(beforeText), "parser", "normalizer", beforeText);
        var after = new DocumentExtraction(await ImportAsync(afterText), "parser", "normalizer", afterText);
        await extractions.SaveAsync(before, CancellationToken.None);
        await extractions.SaveAsync(after, CancellationToken.None);
        return await comparisons.SaveAsync(DocumentComparison.Create(before, after), CancellationToken.None);
    }

    private async Task<CapturedAttemptResult> ImportAsync(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        await using (var file = File.Create(Path.Combine(captureRoot, hash + ".gz")))
        await using (var gzip = new GZipStream(file, CompressionLevel.SmallestSize))
            await gzip.WriteAsync(bytes);
        var request = Request() with { JobId = Guid.NewGuid().ToString("N"), ArtifactDirectory = captureRoot };
        var receipt = Receipt(request) with
        {
            BytesReceived = bytes.Length,
            Capture = new CaptureArtifact { Sha256 = hash, RelativePath = hash + ".gz", ByteLength = bytes.Length },
            Response = new HttpResponseMetadata { StatusCode = 200, ContentType = "text/plain; charset=utf-8", ContentEncodings = [] }
        };
        var result = await attempts.ImportAtomicallyAsync(CollectionAttemptImporter.CreateImport(
            request.JobId, request, receipt), CancellationToken.None);
        return (CapturedAttemptResult)result.AttemptResult;
    }
}
