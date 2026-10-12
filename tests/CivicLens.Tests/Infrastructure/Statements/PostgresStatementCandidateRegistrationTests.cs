using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using CivicLens.Application.Collection;
using CivicLens.Collection.Contracts;
using CivicLens.Core.Collection;
using CivicLens.Core.Documents;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Documents;
using CivicLens.Tests.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;
using static CivicLens.Tests.Fixtures.CollectionFixtures;

namespace CivicLens.Tests.Infrastructure.Statements;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PostgresStatementCandidateRegistrationTests(PostgresCollection postgres) : IAsyncLifetime
{
    private const string ArticleUrl = "https://example.test/pages/article";
    private readonly string schema = "statements_" + Guid.NewGuid().ToString("N");
    private readonly string root = Path.Combine(Path.GetTempPath(), "civic-statements-" + Guid.NewGuid().ToString("N"));
    private string connectionString = null!;
    private PostgresCollectionAttemptStore attempts = null!;
    private PostgresDocumentExtractionStore extractions = null!;

    public async Task InitializeAsync()
    {
        await ExecuteAsync(postgres.ConnectionString, $"CREATE SCHEMA {schema}");
        connectionString = new NpgsqlConnectionStringBuilder(postgres.ConnectionString) { SearchPath = schema }.ConnectionString;
        var factory = new PooledDbContextFactory<CollectionAttemptDbContext>(
            new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options);
        attempts = new PostgresCollectionAttemptStore(factory);
        extractions = new PostgresDocumentExtractionStore(factory);
        await attempts.MigrateAsync();
        Directory.CreateDirectory(root);
    }

    public async Task DisposeAsync()
    {
        await ExecuteAsync(postgres.ConnectionString, $"DROP SCHEMA IF EXISTS {schema} CASCADE");
        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public async Task ExtractionFromADiscoveryAdmittedJobRegistersOneCandidateAndOnePendingGateRow()
    {
        var attempt = await ImportAsync("Press release.", ArticleUrl, JobKind.DiscoveryAdmitted);
        var extraction = new DocumentExtraction(attempt, "parser-v1", "normalization-v1", "Press release.");

        await extractions.SaveAsync(extraction, CancellationToken.None);

        Assert.Equal([(attempt.SourceId, ArticleUrl, extraction.ExtractionId, attempt.AttemptId)], await CandidatesAsync());
        Assert.Equal([("gate", "Pending", 0L, 0, 0L)], await AnalysisRowsAsync());
    }

    [Fact]
    public async Task ExtractionFromAWatchedPageJobRegistersNoCandidate()
    {
        var attempt = await ImportAsync("Policy page.", ArticleUrl, JobKind.WatchedPage);

        await extractions.SaveAsync(new DocumentExtraction(attempt, "p", "n", "Policy page."), CancellationToken.None);

        Assert.Empty(await CandidatesAsync());
        Assert.Empty(await AnalysisRowsAsync());
    }

    [Fact]
    public async Task ExtractionWithoutAnyJobRegistersNoCandidate()
    {
        var attempt = await ImportAsync("Imported by hand.", ArticleUrl, JobKind.None);

        await extractions.SaveAsync(new DocumentExtraction(attempt, "p", "n", "Imported by hand."), CancellationToken.None);

        Assert.Empty(await CandidatesAsync());
    }

    [Fact]
    public async Task RefetchAndParserUpgradeForTheSameSourceAndUrlKeepTheFirstCandidate()
    {
        var first = await ImportAsync("First text.", ArticleUrl, JobKind.DiscoveryAdmitted);
        var firstExtraction = new DocumentExtraction(first, "parser-v1", "n", "First text.");
        await extractions.SaveAsync(firstExtraction, CancellationToken.None);
        await extractions.SaveAsync(new DocumentExtraction(first, "parser-v2", "n", "First text, reparsed."), CancellationToken.None);
        var refetch = await ImportAsync("Edited text.", ArticleUrl, JobKind.DiscoveryAdmitted);

        await extractions.SaveAsync(new DocumentExtraction(refetch, "parser-v2", "n", "Edited text."), CancellationToken.None);

        Assert.Equal([(first.SourceId, ArticleUrl, firstExtraction.ExtractionId, first.AttemptId)], await CandidatesAsync());
        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM statement_analysis"));
        Assert.Equal(3L, await ScalarAsync("SELECT count(*) FROM document_extractions"));
    }

    [Fact]
    public async Task TheSameUrlOnADifferentSourceIsAnotherCandidate()
    {
        var one = await ImportAsync("Text one.", ArticleUrl, JobKind.DiscoveryAdmitted, sourceId: "source-one");
        var two = await ImportAsync("Text two.", ArticleUrl, JobKind.DiscoveryAdmitted, sourceId: "source-two");

        await extractions.SaveAsync(new DocumentExtraction(one, "p", "n", "Text one."), CancellationToken.None);
        await extractions.SaveAsync(new DocumentExtraction(two, "p", "n", "Text two."), CancellationToken.None);

        Assert.Equal(2L, await ScalarAsync("SELECT count(*) FROM statement_candidates"));
    }

    [Fact]
    public async Task ConcurrentSavesForTheSameSourceAndUrlLeaveOneCandidate()
    {
        var saves = new List<DocumentExtraction>();
        for (var index = 0; index < 6; index++)
        {
            var text = "Version " + index;
            saves.Add(new DocumentExtraction(await ImportAsync(text, ArticleUrl, JobKind.DiscoveryAdmitted), "p", "n", text));
        }

        await Task.WhenAll(saves.Select(extraction => extractions.SaveAsync(extraction, CancellationToken.None)));

        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM statement_candidates"));
        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM statement_analysis"));
        Assert.Equal(6L, await ScalarAsync("SELECT count(*) FROM document_extractions"));
    }

    [Fact]
    public async Task FailedGateInsertRollsBackTheExtractionAndTheCandidate()
    {
        await ExecuteAsync(connectionString, """
            CREATE FUNCTION fail_gate() RETURNS trigger AS $$ BEGIN RAISE EXCEPTION 'gate refused'; END; $$ LANGUAGE plpgsql;
            CREATE TRIGGER trg_fail_gate BEFORE INSERT ON statement_analysis
            FOR EACH ROW EXECUTE FUNCTION fail_gate();
            """);
        var attempt = await ImportAsync("Press release.", ArticleUrl, JobKind.DiscoveryAdmitted);

        await Assert.ThrowsAnyAsync<Exception>(() => extractions.SaveAsync(
            new DocumentExtraction(attempt, "p", "n", "Press release."), CancellationToken.None));

        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM document_extractions"));
        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM statement_candidates"));
    }

    [Fact]
    public async Task ANewGateRowWakesTheAnalysisListener()
    {
        await using var listener = new NpgsqlConnection(connectionString);
        await listener.OpenAsync();
        var notified = new TaskCompletionSource();
        listener.Notification += (_, _) => notified.TrySetResult();
        await ExecuteAsync(listener, "LISTEN civic_lens_analysis");
        var attempt = await ImportAsync("Press release.", ArticleUrl, JobKind.DiscoveryAdmitted);

        await extractions.SaveAsync(new DocumentExtraction(attempt, "p", "n", "Press release."), CancellationToken.None);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var waiting = listener.WaitAsync(timeout.Token);

        await Task.WhenAny(notified.Task, waiting);
        Assert.True(notified.Task.IsCompleted, "Queueing a gate row must send the analysis wakeup.");
    }

    private enum JobKind { None, DiscoveryAdmitted, WatchedPage }

    private async Task<CapturedAttemptResult> ImportAsync(string text, string url, JobKind kind, string sourceId = "source")
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        await using (var file = File.Create(Path.Combine(root, hash + ".gz")))
        await using (var gzip = new GZipStream(file, CompressionLevel.SmallestSize))
            await gzip.WriteAsync(bytes);
        var attemptId = Guid.NewGuid().ToString("N");
        var request = Request() with { JobId = attemptId, SourceId = sourceId, Url = url, ArtifactDirectory = root };
        var receipt = Receipt(request) with
        {
            BytesReceived = bytes.Length,
            Capture = new CaptureArtifact { Sha256 = hash, RelativePath = hash + ".gz", ByteLength = bytes.Length },
            Response = new HttpResponseMetadata { StatusCode = 200, ContentType = "text/plain; charset=utf-8", ContentEncodings = [] }
        };
        var imported = await attempts.ImportAtomicallyAsync(CollectionAttemptImporter.CreateImport(attemptId, request, receipt),
            CancellationToken.None);
        if (kind != JobKind.None) await SeedJobAttemptAsync(attemptId, kind);
        return (CapturedAttemptResult)imported.AttemptResult;
    }

    private Task SeedJobAttemptAsync(string attemptId, JobKind kind)
    {
        var key = kind == JobKind.DiscoveryAdmitted ? "discovery-admission/" + attemptId : "watched-page/" + attemptId;
        return ExecuteAsync(connectionString, $$"""
            INSERT INTO collection_jobs (job_id, idempotency_key, definition_json, state, created_at, cancellation_requested,
                charged_requests, charged_bytes, charged_seconds, lease_fence)
            VALUES ('{{attemptId}}', '{{key}}', '{}', 'Succeeded', 0, false, 0, 0, 0, 0);
            INSERT INTO collection_job_attempts (attempt_id, job_id, sequence, request_json, started_at)
            VALUES ('{{attemptId}}', '{{attemptId}}', 1, '{}', 0);
            """);
    }

    private async Task<List<(string Source, string Url, string Extraction, string Attempt)>> CandidatesAsync()
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT source_id, url, extraction_id, attempt_id FROM statement_candidates ORDER BY candidate_id", connection);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<(string, string, string, string)>();
        while (await reader.ReadAsync()) rows.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
        return rows;
    }

    private async Task<List<(string Task, string Status, long Fence, int Attempts, long Reserved)>> AnalysisRowsAsync()
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT task, status, fence, attempts, reserved_tokens FROM statement_analysis ORDER BY task", connection);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<(string, string, long, int, long)>();
        while (await reader.ReadAsync()) rows.Add((reader.GetString(0), reader.GetString(1), reader.GetInt64(2), reader.GetInt32(3), reader.GetInt64(4)));
        return rows;
    }

    private async Task<long> ScalarAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await ExecuteAsync(connection, sql);
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
