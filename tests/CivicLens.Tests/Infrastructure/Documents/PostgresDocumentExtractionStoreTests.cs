using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CivicLens.Application.Collection;
using CivicLens.Collection.Contracts;
using CivicLens.Core.Collection;
using CivicLens.Core.Documents;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Documents;
using CivicLens.Tests.Host;
using CivicLens.Tests.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;
using static CivicLens.Tests.Fixtures.CollectionFixtures;

namespace CivicLens.Tests.Infrastructure.Documents;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PostgresDocumentExtractionStoreTests(PostgresCollection postgres) : IAsyncLifetime
{
    private readonly string schema = "documents_" + Guid.NewGuid().ToString("N");
    private readonly string root = Path.Combine(Path.GetTempPath(), "civic-documents-" + Guid.NewGuid().ToString("N"));
    private string connectionString = null!;
    private PooledDbContextFactory<CollectionAttemptDbContext> factory = null!;
    private PostgresCollectionAttemptStore attempts = null!;
    private PostgresDocumentExtractionStore extractions = null!;

    public async Task InitializeAsync()
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE SCHEMA {schema}";
        await command.ExecuteNonQueryAsync();
        connectionString = new NpgsqlConnectionStringBuilder(postgres.ConnectionString) { SearchPath = schema }.ConnectionString;
        factory = new PooledDbContextFactory<CollectionAttemptDbContext>(
            new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options);
        attempts = new PostgresCollectionAttemptStore(factory);
        extractions = new PostgresDocumentExtractionStore(factory);
        await attempts.MigrateAsync();
        Directory.CreateDirectory(root);
    }

    public async Task DisposeAsync()
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"DROP SCHEMA IF EXISTS {schema} CASCADE";
        await command.ExecuteNonQueryAsync();
        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public async Task ConcurrentReplayPreservesOneRecordAndParserUpgradePreservesPriorText()
    {
        var attempt = await ImportAsync("source text");
        var extraction = new DocumentExtraction(attempt, "parser-v1", "normalization-v1", "source text");
        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => extractions.SaveAsync(extraction, CancellationToken.None)));
        Assert.All(results, result => Assert.Equal(extraction.TextSha256, result.TextSha256));
        var upgraded = new DocumentExtraction(attempt, "parser-v2", "normalization-v1", "reprocessed text");
        await extractions.SaveAsync(upgraded, CancellationToken.None);
        Assert.NotEqual(extraction.ExtractionId, upgraded.ExtractionId);
        Assert.Equal("source text", (await extractions.GetAsync(extraction.ExtractionId, CancellationToken.None))!.Text);
        await using var db = await factory.CreateDbContextAsync();
        Assert.Equal(2, await db.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM document_extractions").SingleAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => extractions.SaveAsync(
            new DocumentExtraction(attempt, "parser-v1", "normalization-v1", "conflicting text"), CancellationToken.None));
        Assert.Equal("source text", (await extractions.GetAsync(extraction.ExtractionId, CancellationToken.None))!.Text);
    }

    [Fact]
    public async Task RejectsChangedProvenanceAndDetectsStoredTextTampering()
    {
        var attempt = await ImportAsync("source text");
        var changed = new CapturedAttemptResult(attempt.AttemptId, "other-source", attempt.RequestedUrl,
            attempt.FinalUrl, attempt.ObservedAt, attempt.Response, attempt.Capture);
        var invalid = new DocumentExtraction(changed, "p", "n", "source text");
        await Assert.ThrowsAsync<InvalidOperationException>(() => extractions.SaveAsync(invalid, CancellationToken.None));
        Assert.Null(await extractions.GetAsync(invalid.ExtractionId, CancellationToken.None));
        var valid = new DocumentExtraction(attempt, "p", "n", "source text");
        await extractions.SaveAsync(valid, CancellationToken.None);
        await using var db = await factory.CreateDbContextAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE document_extractions SET text = 'tampered' WHERE extraction_id = {valid.ExtractionId}");
        await Assert.ThrowsAsync<InvalidOperationException>(() => extractions.GetAsync(valid.ExtractionId, CancellationToken.None));
    }

    [Fact]
    public async Task FailedInsertAndCancellationLeaveNoRecordAndCanBeRetried()
    {
        var attempt = await ImportAsync("source text");
        var extraction = new DocumentExtraction(attempt, "p", "n", "source text");
        await using var db = await factory.CreateDbContextAsync();
        await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION fail_extraction() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'injected extraction failure'; END $$;
            CREATE TRIGGER fail_extraction BEFORE INSERT ON document_extractions
            FOR EACH ROW EXECUTE FUNCTION fail_extraction();
            """);
        await Assert.ThrowsAsync<DbUpdateException>(() => extractions.SaveAsync(extraction, CancellationToken.None));
        Assert.Null(await extractions.GetAsync(extraction.ExtractionId, CancellationToken.None));
        await db.Database.ExecuteSqlRawAsync("DROP TRIGGER fail_extraction ON document_extractions");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => extractions.SaveAsync(extraction, cancellation.Token));
        Assert.Null(await extractions.GetAsync(extraction.ExtractionId, CancellationToken.None));
        Assert.Equal(extraction.ExtractionId, (await extractions.SaveAsync(extraction, CancellationToken.None)).ExtractionId);
    }

    [Fact]
    public async Task CliExtractsInspectsAndCitesExactTextAndRejectsCorruptedReplay()
    {
        var attempt = await ImportAsync("A😀 source text");
        var extracted = await HostProcess.RunAsync(connectionString, "documents", "extract", attempt.AttemptId, root);
        Assert.Equal(0, extracted.ExitCode);
        using var summary = JsonDocument.Parse(extracted.Output);
        var id = summary.RootElement.GetProperty("extractionId").GetString()!;
        var inspected = await HostProcess.RunAsync(connectionString, "documents", "get", id);
        Assert.Equal(0, inspected.ExitCode);
        using var content = JsonDocument.Parse(inspected.Output);
        Assert.Equal("A😀 source text", content.RootElement.GetProperty("text").GetString());
        var citation = await HostProcess.RunAsync(connectionString, "documents", "cite", id, "1", "2");
        Assert.Equal(0, citation.ExitCode);
        using var quoted = JsonDocument.Parse(citation.Output);
        Assert.Equal("😀", quoted.RootElement.GetProperty("quote").GetString());
        Assert.Equal(2, (await HostProcess.RunAsync(connectionString, "documents", "cite", id, "1", "1")).ExitCode);
        Assert.Equal(0, (await HostProcess.RunAsync(connectionString, "documents", "extract", attempt.AttemptId, root)).ExitCode);
        await File.WriteAllBytesAsync(Path.Combine(root, attempt.Capture.Sha256 + ".gz"), [0, 1, 2]);
        Assert.Equal(1, (await HostProcess.RunAsync(connectionString, "documents", "extract", attempt.AttemptId, root)).ExitCode);
        Assert.Equal("A😀 source text", (await extractions.GetAsync(id, CancellationToken.None))!.Text);
    }

    private async Task<CapturedAttemptResult> ImportAsync(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        await using (var file = File.Create(Path.Combine(root, hash + ".gz")))
        await using (var gzip = new GZipStream(file, CompressionLevel.SmallestSize))
            await gzip.WriteAsync(bytes);
        var request = Request() with { ArtifactDirectory = root };
        var result = Receipt(request) with
        {
            BytesReceived = bytes.Length,
            Capture = new CaptureArtifact { Sha256 = hash, RelativePath = hash + ".gz", ByteLength = bytes.Length },
            Response = new HttpResponseMetadata { StatusCode = 200, ContentType = "text/plain; charset=utf-8", ContentEncodings = [] }
        };
        var imported = await attempts.ImportAtomicallyAsync(CollectionAttemptImporter.CreateImport(
            Guid.NewGuid().ToString("N"), request, result), CancellationToken.None);
        return (CapturedAttemptResult)imported.AttemptResult;
    }
}
