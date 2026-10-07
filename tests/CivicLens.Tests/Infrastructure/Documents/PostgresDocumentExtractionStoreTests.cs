using CivicLens.Application.Documents;
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
using Microsoft.EntityFrameworkCore.Migrations;
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

    [Fact]
    public async Task ConfiguredCliRetainsOldProfileTextAndCitationsAfterProfileChanges()
    {
        var attempt = await ImportAsync("<nav>menu</nav><main>Policy <span class=aside>sidebar</span> text</main>", "text/html; charset=utf-8");
        var config = ProfileConfiguration();
        var configPath = Path.Combine(root, "config.json");
        await File.WriteAllTextAsync(configPath, JsonSerializer.Serialize(config, CollectionProtocol.JsonOptions));
        var first = await HostProcess.RunAsync(connectionString, "documents", "extract", configPath, attempt.AttemptId, root);
        Assert.Equal(0, first.ExitCode);
        using var firstResult = JsonDocument.Parse(first.Output);
        var originalId = firstResult.RootElement.GetProperty("extractionId").GetString()!;
        var original = (await extractions.GetAsync(originalId, CancellationToken.None))!;
        Assert.Equal("Policy text", original.Text);
        Assert.Equal("policy", original.Profile!.Id);
        Assert.Equal(new[] { ".aside" }, original.Profile.ExcludedSelectors);
        Assert.Equal(original.Profile.RevisionId, firstResult.RootElement.GetProperty("profileRevisionId").GetString());
        Assert.Equal(0, (await HostProcess.RunAsync(connectionString, "documents", "extract", configPath, attempt.AttemptId, root)).ExitCode);

        config = config with { DocumentProfiles = [config.DocumentProfiles![0] with { ExcludedSelectors = [] }] };
        await File.WriteAllTextAsync(configPath, JsonSerializer.Serialize(config, CollectionProtocol.JsonOptions));
        var changed = await HostProcess.RunAsync(connectionString, "documents", "extract", configPath, attempt.AttemptId, root);
        Assert.Equal(0, changed.ExitCode);
        using var changedResult = JsonDocument.Parse(changed.Output);
        var changedId = changedResult.RootElement.GetProperty("extractionId").GetString()!;
        Assert.NotEqual(originalId, changedId);
        Assert.Equal("Policy sidebar text", (await extractions.GetAsync(changedId, CancellationToken.None))!.Text);
        var cited = await HostProcess.RunAsync(connectionString, "documents", "cite", originalId, "0", "11");
        Assert.Equal(0, cited.ExitCode);
        using var citation = JsonDocument.Parse(cited.Output);
        Assert.Equal("Policy text", citation.RootElement.GetProperty("quote").GetString());

        config = config with { DocumentProfiles = [config.DocumentProfiles[0] with { Selector = "#missing" }] };
        await File.WriteAllTextAsync(configPath, JsonSerializer.Serialize(config, CollectionProtocol.JsonOptions));
        Assert.Equal(1, (await HostProcess.RunAsync(connectionString, "documents", "extract", configPath, attempt.AttemptId, root)).ExitCode);
        await using var db = await factory.CreateDbContextAsync();
        Assert.Equal(2, await db.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM document_extractions").SingleAsync());
    }

    [Fact]
    public async Task StoredProfileTamperingCannotRelabelExistingExtraction()
    {
        var attempt = await ImportAsync("text");
        var profile = new DocumentContentProfile("policy", "main", [".aside"]);
        var original = new DocumentExtraction(attempt, "p", "n", "text", profile);
        await extractions.SaveAsync(original, CancellationToken.None);
        await using var db = await factory.CreateDbContextAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE document_extractions SET profile_json = NULL WHERE extraction_id = {original.ExtractionId}");
        await Assert.ThrowsAsync<InvalidOperationException>(() => extractions.GetAsync(original.ExtractionId, CancellationToken.None));
    }

    [Fact]
    public async Task ProfileMigrationPreservesExistingExtractionAndCitationIdentity()
    {
        await using var db = await factory.CreateDbContextAsync();
        var attempt = await ImportAsync("old text");
        await db.GetService<IMigrator>().MigrateAsync("20261007161805_DocumentExtractions");
        var original = new DocumentExtraction(attempt, "p", "n", "old text");
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO document_extractions (extraction_id, attempt_id, parser_version, normalization_version, text, text_sha256)
            VALUES ({original.ExtractionId}, {attempt.AttemptId}, {original.ParserVersion}, {original.NormalizationVersion}, {original.Text}, {original.TextSha256})
            """);
        await attempts.MigrateAsync();
        var retained = (await extractions.GetAsync(original.ExtractionId, CancellationToken.None))!;
        Assert.Null(retained.Profile);
        Assert.Equal(original.Text, retained.Text);
        Assert.Equal(original.ExtractionId, retained.ExtractionId);
        Assert.Equal("old text", new DocumentTextSpan(retained, 0, retained.Text.Length).Quote);
    }

    [Fact]
    public async Task ComparisonReplayBindsRetainedEvidenceAndDetectsTampering()
    {
        var before = new DocumentExtraction(await ImportAsync("Old policy."), "p", "n", "Old policy.");
        var after = new DocumentExtraction(await ImportAsync("New policy."), "p", "n", "New policy.");
        await extractions.SaveAsync(before, CancellationToken.None);
        await extractions.SaveAsync(after, CancellationToken.None);
        var comparisons = new PostgresDocumentComparisonStore(factory);
        var comparison = DocumentComparison.Create(before, after);
        var saved = await Task.WhenAll(Enumerable.Range(0, 4)
            .Select(_ => comparisons.SaveAsync(comparison, CancellationToken.None)));
        Assert.All(saved, item => Assert.Equal(comparison.ComparisonId, item.ComparisonId));
        Assert.Equal(DocumentComparisonStatus.Complete,
            (await comparisons.GetAsync(comparison.ComparisonId, CancellationToken.None))!.Status);
        var forged = DocumentComparison.Create(new DocumentExtraction(before.SourceAttempt, "p", "n", "Forged policy."), after);
        await Assert.ThrowsAsync<InvalidOperationException>(() => comparisons.SaveAsync(forged, CancellationToken.None));
        await using var db = await factory.CreateDbContextAsync();
        Assert.Equal(1, await db.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM document_comparisons").SingleAsync());
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE document_comparisons SET before_extraction_id = {after.ExtractionId} WHERE comparison_id = {comparison.ComparisonId}");
        await Assert.ThrowsAsync<InvalidOperationException>(() => comparisons.SaveAsync(comparison, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => comparisons.GetAsync(comparison.ComparisonId, CancellationToken.None));
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE document_comparisons SET before_extraction_id = {before.ExtractionId} WHERE comparison_id = {comparison.ComparisonId}");
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE document_comparisons SET result_json = '{{}}' WHERE comparison_id = {comparison.ComparisonId}");
        await Assert.ThrowsAsync<InvalidOperationException>(() => comparisons.GetAsync(comparison.ComparisonId, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => comparisons.SaveAsync(comparison, CancellationToken.None));
    }

    [Fact]
    public async Task HistoryRetainsReversionsSettingsAndResolvedChecksAndRejectsOverflow()
    {
        var first = await ImportAsync("A");
        var second = await ImportAsync("B");
        var third = await ImportAsync("A", etag: "\"v3\"");
        foreach (var pair in new[] { (first, "A"), (second, "B"), (third, "A") })
            await extractions.SaveAsync(new DocumentExtraction(pair.Item1, "p", "n", pair.Item2), CancellationToken.None);
        await extractions.SaveAsync(new DocumentExtraction(first, "p2", "n", "reprocessed"), CancellationToken.None);
        var request = Request() with { ETag = "\"v3\"" };
        var receipt = Receipt(request) with
        {
            Outcome = CollectionOutcome.NotModified,
            SentValidators = new HttpRequestValidators { ETag = request.ETag },
            Capture = null,
            BytesReceived = 0,
            Response = new HttpResponseMetadata { StatusCode = 304, ETag = "\"v3\"", ContentEncodings = [] }
        };
        // A matching validator links this check to the third capture without creating new text.
        await attempts.ImportAtomicallyAsync(CollectionAttemptImporter.CreateImport("resolved", request, receipt), CancellationToken.None);
        var store = new PostgresDocumentHistoryStore(factory);
        var history = await new GetDocumentHistory(store).ExecuteAsync(first.SourceId, first.RequestedUrl);
        Assert.Equal(4, history.Observations.Length);
        Assert.Equal(new[] { "A", "B", "A" }, history.Streams.Single(stream => stream.ParserVersion == "p").Transitions.Select(item => item.Text));
        Assert.Equal(third.AttemptId, Assert.Single(history.Observations[^1].Extractions).SourceAttempt.AttemptId);
        Assert.Equal(2, history.Streams.Single(stream => stream.ParserVersion == "p").Transitions[^1].AttemptIds.Length);
        Assert.Single(history.Streams.Single(stream => stream.ParserVersion == "p2").Transitions);
        Assert.Empty((await new GetDocumentHistory(store).ExecuteAsync(first.SourceId, first.RequestedUrl + "?other")).Observations);
        await Assert.ThrowsAsync<DocumentHistoryLimitException>(() => store.GetAsync(first.SourceId, first.RequestedUrl, 2, CancellationToken.None));
    }

    [Fact]
    public async Task HistoryTimestampTiesUseOrdinalUtf16AttemptOrder()
    {
        var request = Request();
        var receipt = Receipt(request);
        foreach (var id in new[] { "\ue000", "\U00010000" })
            await attempts.ImportAtomicallyAsync(CollectionAttemptImporter.CreateImport(id, request, receipt), CancellationToken.None);
        var history = await new GetDocumentHistory(new PostgresDocumentHistoryStore(factory))
            .ExecuteAsync(request.SourceId, request.Url);
        Assert.Equal(new[] { "\U00010000", "\ue000" }, history.Observations.Select(item => item.Attempt.AttemptResult.AttemptId));
    }

    [Fact]
    public async Task DocumentHistoryDoesNotLoadDiscoveryPayloads()
    {
        var request = Request() with { Mode = CollectionMode.Html, MaxCandidates = 1000 };
        var receipt = Receipt(request) with
        {
            Discovery = new DiscoveryResult
            {
                Status = DiscoveryStatus.Parsed,
                Urls = Enumerable.Range(0, 1000).Select(index => request.Url + "/" + index + new string('a', 3000)).ToArray()
            }
        };
        var imported = await attempts.ImportAtomicallyAsync(CollectionAttemptImporter.CreateImport("discovery", request, receipt), CancellationToken.None);
        var captured = Assert.IsType<CapturedAttemptResult>(imported.AttemptResult);
        await extractions.SaveAsync(new DocumentExtraction(captured, "p", "n", "text"), CancellationToken.None);
        Assert.Equal(1000, (await attempts.GetAsync("discovery", CancellationToken.None))!.Discovery!.Urls.Length);
        var history = await new GetDocumentHistory(new PostgresDocumentHistoryStore(factory))
            .ExecuteAsync(request.SourceId, request.Url);
        var observation = Assert.Single(history.Observations);
        Assert.Null(observation.Attempt.Discovery);
        Assert.Equal("text", Assert.Single(observation.Extractions).Text);
    }

    [Fact]
    public async Task CliComparesInspectsAndListsHistoryWithExplicitIncompatibility()
    {
        var before = new DocumentExtraction(await ImportAsync("Old policy."), "p", "n", "Old policy.");
        var after = new DocumentExtraction(await ImportAsync("New policy."), "p", "n", "New policy.");
        await extractions.SaveAsync(before, CancellationToken.None);
        await extractions.SaveAsync(after, CancellationToken.None);
        var output = await HostProcess.RunAsync(connectionString, "documents", "compare", before.ExtractionId, after.ExtractionId);
        Assert.Equal(0, output.ExitCode);
        using var comparison = JsonDocument.Parse(output.Output);
        var id = comparison.RootElement.GetProperty("comparisonId").GetString()!;
        var inspection = await HostProcess.RunAsync(connectionString, "documents", "comparison", id);
        Assert.Equal(0, inspection.ExitCode);
        Assert.Equal(output.Output, inspection.Output);
        var history = await HostProcess.RunAsync(connectionString, "documents", "history", before.SourceAttempt.SourceId, before.SourceAttempt.RequestedUrl);
        Assert.Equal(0, history.ExitCode);
        using var historyJson = JsonDocument.Parse(history.Output);
        Assert.Equal(2, historyJson.RootElement.GetProperty("observations").GetArrayLength());
        var upgraded = new DocumentExtraction(after.SourceAttempt, "p2", "n", "New policy.");
        await extractions.SaveAsync(upgraded, CancellationToken.None);
        var incompatible = await HostProcess.RunAsync(connectionString, "documents", "compare", before.ExtractionId, upgraded.ExtractionId);
        Assert.Equal(1, incompatible.ExitCode);
        using var failure = JsonDocument.Parse(incompatible.Output);
        Assert.Equal("incompatible", failure.RootElement.GetProperty("status").GetString());
        Assert.Empty(failure.RootElement.GetProperty("hunks").EnumerateArray());
    }

    private static CollectionConfiguration ProfileConfiguration() => new()
    {
        Version = 2,
        People = [new PersonConfiguration { Id = "person", Name = "Fixture Official" }],
        DocumentProfiles = [new DocumentProfileConfiguration { Id = "policy", Selector = "main", ExcludedSelectors = [".aside"] }],
        Sources = [new WatchedSourceConfiguration
        {
            Id = "source", Coverage = [new SourceCoverageConfiguration { PersonId = "person" }],
            Url = "https://example.test/pages/a", AllowedOrigin = "https://example.test", AllowedPathPrefix = "/pages",
            DocumentProfileId = "policy", Enabled = false
        }]
    };

    private async Task<CapturedAttemptResult> ImportAsync(string text, string contentType = "text/plain; charset=utf-8", string? etag = null)
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
            Response = new HttpResponseMetadata { StatusCode = 200, ContentType = contentType, ETag = etag, ContentEncodings = [] }
        };
        var imported = await attempts.ImportAtomicallyAsync(CollectionAttemptImporter.CreateImport(
            Guid.NewGuid().ToString("N"), request, result), CancellationToken.None);
        return (CapturedAttemptResult)imported.AttemptResult;
    }
}
