using CivicLens.Core.Documents;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Documents;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.Statements;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PostgresStatementCandidateRegistrationTests(PostgresCollection postgres) : IAsyncLifetime
{
    private const string ArticleUrl = "https://example.test/pages/article";
    private readonly string root = Path.Combine(Path.GetTempPath(), "civic-statements-" + Guid.NewGuid().ToString("N"));
    private string connectionString = null!;
    private PostgresCollectionAttemptStore attempts = null!;
    private PostgresDocumentExtractionStore extractions = null!;
    private StatementCandidateSeeder seeder = null!;

    public async Task InitializeAsync()
    {
        connectionString = await postgres.CreateMigratedDatabaseAsync();
        var factory = new PooledDbContextFactory<CollectionAttemptDbContext>(
            new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options);
        attempts = new PostgresCollectionAttemptStore(factory);
        extractions = new PostgresDocumentExtractionStore(factory);
        seeder = new StatementCandidateSeeder(attempts, extractions, connectionString, root);
        Directory.CreateDirectory(root);
    }

    public async Task DisposeAsync()
    {
        await postgres.DropDatabaseAsync(connectionString);
        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public async Task ExtractionFromADiscoveryAdmittedJobRegistersOneCandidateAndOnePendingGateRow()
    {
        var attempt = await seeder.ImportAsync("Press release.", ArticleUrl, CandidateJobKind.DiscoveryAdmitted);
        var extraction = new DocumentExtraction(attempt, "parser-v1", "normalization-v1", "Press release.");

        await extractions.SaveAsync(extraction, CancellationToken.None);

        Assert.Equal([(attempt.SourceId, ArticleUrl, extraction.ExtractionId, attempt.AttemptId)], await CandidatesAsync());
        Assert.Equal([("gate", "Pending", 0L, 0, 0L)], await AnalysisRowsAsync());
        Assert.Equal(1L, await ScalarAsync("SELECT priority::bigint FROM statement_analysis"));
    }

    [Fact]
    public async Task ExtractionFromAWatchedPageJobRegistersNoCandidate()
    {
        var attempt = await seeder.ImportAsync("Policy page.", ArticleUrl, CandidateJobKind.WatchedPage);

        await extractions.SaveAsync(new DocumentExtraction(attempt, "p", "n", "Policy page."), CancellationToken.None);

        Assert.Empty(await CandidatesAsync());
        Assert.Empty(await AnalysisRowsAsync());
    }

    [Fact]
    public async Task ExtractionFromABillStreamJobRegistersNoCandidateBecauseBillsAreNotStatements()
    {
        var attempt = await seeder.ImportAsync("Bill text.", ArticleUrl, CandidateJobKind.BillStream);

        await extractions.SaveAsync(new DocumentExtraction(attempt, "p", "n", "Bill text."), CancellationToken.None);

        Assert.Empty(await CandidatesAsync());
        Assert.Empty(await AnalysisRowsAsync());
    }

    [Fact]
    public async Task ExtractionWithoutAnyJobRegistersNoCandidate()
    {
        var attempt = await seeder.ImportAsync("Imported by hand.", ArticleUrl, CandidateJobKind.None);

        await extractions.SaveAsync(new DocumentExtraction(attempt, "p", "n", "Imported by hand."), CancellationToken.None);

        Assert.Empty(await CandidatesAsync());
    }

    [Fact]
    public async Task RefetchAndParserUpgradeForTheSameSourceAndUrlKeepTheFirstCandidate()
    {
        var first = await seeder.ImportAsync("First text.", ArticleUrl, CandidateJobKind.DiscoveryAdmitted);
        var firstExtraction = new DocumentExtraction(first, "parser-v1", "n", "First text.");
        await extractions.SaveAsync(firstExtraction, CancellationToken.None);
        await extractions.SaveAsync(new DocumentExtraction(first, "parser-v2", "n", "First text, reparsed."), CancellationToken.None);
        var refetch = await seeder.ImportAsync("Edited text.", ArticleUrl, CandidateJobKind.DiscoveryAdmitted);

        await extractions.SaveAsync(new DocumentExtraction(refetch, "parser-v2", "n", "Edited text."), CancellationToken.None);

        Assert.Equal([(first.SourceId, ArticleUrl, firstExtraction.ExtractionId, first.AttemptId)], await CandidatesAsync());
        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM statement_analysis"));
        Assert.Equal(3L, await ScalarAsync("SELECT count(*) FROM document_extractions"));
    }

    [Fact]
    public async Task TheSameUrlOnADifferentSourceIsAnotherCandidate()
    {
        var one = await seeder.ImportAsync("Text one.", ArticleUrl, CandidateJobKind.DiscoveryAdmitted, sourceId: "source-one");
        var two = await seeder.ImportAsync("Text two.", ArticleUrl, CandidateJobKind.DiscoveryAdmitted, sourceId: "source-two");

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
            saves.Add(new DocumentExtraction(await seeder.ImportAsync(text, ArticleUrl, CandidateJobKind.DiscoveryAdmitted), "p", "n", text));
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
        var attempt = await seeder.ImportAsync("Press release.", ArticleUrl, CandidateJobKind.DiscoveryAdmitted);

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
        var attempt = await seeder.ImportAsync("Press release.", ArticleUrl, CandidateJobKind.DiscoveryAdmitted);

        await extractions.SaveAsync(new DocumentExtraction(attempt, "p", "n", "Press release."), CancellationToken.None);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var waiting = listener.WaitAsync(timeout.Token);

        await Task.WhenAny(notified.Task, waiting);
        Assert.True(notified.Task.IsCompleted, "Queueing a gate row must send the analysis wakeup.");
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
