using CivicLens.Application.Documents;
using CivicLens.Core.Collection;
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
    private const string FeedUrl = "https://example.test/pages/feed";
    private static readonly DateTimeOffset In119th = new(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
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
        var extraction = new DocumentExtraction(attempt, "parser-v1", "Press release.");

        await extractions.SaveAsync(extraction, CancellationToken.None);

        Assert.Equal([(attempt.SourceId, ArticleUrl, extraction.ExtractionId, attempt.AttemptId)], await CandidatesAsync());
        Assert.Equal([("gate", "Pending", 0L, 0, 0L)], await AnalysisRowsAsync());
        Assert.Equal(1L, await ScalarAsync("SELECT priority::bigint FROM statement_analysis"));
    }

    [Fact]
    public async Task ExtractionFromAWatchedPageJobRegistersNoCandidate()
    {
        var attempt = await seeder.ImportAsync("Policy page.", ArticleUrl, CandidateJobKind.WatchedPage);

        await extractions.SaveAsync(new DocumentExtraction(attempt, "p", "Policy page."), CancellationToken.None);

        Assert.Empty(await CandidatesAsync());
        Assert.Empty(await AnalysisRowsAsync());
    }

    [Fact]
    public async Task ExtractionFromABillStreamJobRegistersNoCandidateBecauseBillsAreNotStatements()
    {
        var attempt = await seeder.ImportAsync("Bill text.", ArticleUrl, CandidateJobKind.BillStream);

        await extractions.SaveAsync(new DocumentExtraction(attempt, "p", "Bill text."), CancellationToken.None);

        Assert.Empty(await CandidatesAsync());
        Assert.Empty(await AnalysisRowsAsync());
    }

    [Fact]
    public async Task ExtractionWithoutAnyJobRegistersNoCandidate()
    {
        var attempt = await seeder.ImportAsync("Imported by hand.", ArticleUrl, CandidateJobKind.None);

        await extractions.SaveAsync(new DocumentExtraction(attempt, "p", "Imported by hand."), CancellationToken.None);

        Assert.Empty(await CandidatesAsync());
    }

    [Fact]
    public async Task RefetchAndParserUpgradeForTheSameSourceAndUrlKeepTheFirstCandidate()
    {
        var first = await seeder.ImportAsync("First text.", ArticleUrl, CandidateJobKind.DiscoveryAdmitted);
        var firstExtraction = new DocumentExtraction(first, "parser-v1", "First text.");
        await extractions.SaveAsync(firstExtraction, CancellationToken.None);
        await extractions.SaveAsync(new DocumentExtraction(first, "parser-v2", "First text, reparsed."), CancellationToken.None);
        var refetch = await seeder.ImportAsync("Edited text.", ArticleUrl, CandidateJobKind.DiscoveryAdmitted);

        await extractions.SaveAsync(new DocumentExtraction(refetch, "parser-v2", "Edited text."), CancellationToken.None);

        Assert.Equal([(first.SourceId, ArticleUrl, firstExtraction.ExtractionId, first.AttemptId)], await CandidatesAsync());
        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM statement_analysis"));
        Assert.Equal(3L, await ScalarAsync("SELECT count(*) FROM document_extractions"));
    }

    [Fact]
    public async Task TheSameUrlOnADifferentSourceIsAnotherCandidate()
    {
        var one = await seeder.ImportAsync("Text one.", ArticleUrl, CandidateJobKind.DiscoveryAdmitted, sourceId: "source-one");
        var two = await seeder.ImportAsync("Text two.", ArticleUrl, CandidateJobKind.DiscoveryAdmitted, sourceId: "source-two");

        await extractions.SaveAsync(new DocumentExtraction(one, "p", "Text one."), CancellationToken.None);
        await extractions.SaveAsync(new DocumentExtraction(two, "p", "Text two."), CancellationToken.None);

        Assert.Equal(2L, await ScalarAsync("SELECT count(*) FROM statement_candidates"));
    }

    [Fact]
    public async Task ConcurrentSavesForTheSameSourceAndUrlLeaveOneCandidate()
    {
        var saves = new List<DocumentExtraction>();
        for (var index = 0; index < 6; index++)
        {
            var text = "Version " + index;
            saves.Add(new DocumentExtraction(await seeder.ImportAsync(text, ArticleUrl, CandidateJobKind.DiscoveryAdmitted), "p", text));
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
            new DocumentExtraction(attempt, "p", "Press release."), CancellationToken.None));

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

        await extractions.SaveAsync(new DocumentExtraction(attempt, "p", "Press release."), CancellationToken.None);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var waiting = listener.WaitAsync(timeout.Token);

        await Task.WhenAny(notified.Task, waiting);
        Assert.True(notified.Task.IsCompleted, "Queueing a gate row must send the analysis wakeup.");
    }

    [Fact]
    public async Task ANewStatementCandidateRaisesAMatchingVoteRowAndLeavesTheVoteOnAnotherBillAlone()
    {
        await SeedVoteRowAsync(new string('1', 64), roll: 1, congress: 119, billNumber: 14);
        await SeedVoteRowAsync(new string('2', 64), roll: 2, congress: 119, billNumber: 15);
        var text = "Said: I back H.Con.Res. 14 today.";
        var attempt = await seeder.ImportAsync(text, ArticleUrl, CandidateJobKind.DiscoveryAdmitted, observedAt: In119th);

        await extractions.SaveAsync(new DocumentExtraction(attempt, "parser-v1", text), CancellationToken.None);

        Assert.Equal(1L, await ScalarAsync($"SELECT priority::bigint FROM vote_analysis WHERE candidate_id = '{new string('1', 64)}'"));
        Assert.Equal(3L, await ScalarAsync($"SELECT priority::bigint FROM vote_analysis WHERE candidate_id = '{new string('2', 64)}'"));
    }

    [Fact]
    public async Task ACaptureInAnotherCongressRaisesNoRowThatHasTheSameBillNumber()
    {
        await SeedVoteRowAsync(new string('1', 64), roll: 1, congress: 118, billNumber: 14);
        await SeedVoteRowAsync(new string('2', 64), roll: 2, congress: 119, billNumber: 14);
        var text = "Said: I back H.Con.Res. 14 today.";
        var attempt = await seeder.ImportAsync(text, ArticleUrl, CandidateJobKind.DiscoveryAdmitted, observedAt: In119th);

        await extractions.SaveAsync(new DocumentExtraction(attempt, "parser-v1", text), CancellationToken.None);

        Assert.Equal(3L, await ScalarAsync($"SELECT priority::bigint FROM vote_analysis WHERE candidate_id = '{new string('1', 64)}'"));
        Assert.Equal(1L, await ScalarAsync($"SELECT priority::bigint FROM vote_analysis WHERE candidate_id = '{new string('2', 64)}'"));
    }

    [Fact]
    public async Task FeedItemsRegisterNewestFirstUpToTheLimitSoOneBusyFeedCannotFloodTheGate()
    {
        var feed = await seeder.ImportAsync("feed", FeedUrl, CandidateJobKind.None);
        var items = Items(feed, "a", "b", "c");

        var registered = await extractions.SaveFeedItemsAsync(items, 2, CancellationToken.None);

        Assert.Equal(2, registered);
        Assert.Equal([(feed.SourceId, ItemUrl("a"), items[0].Extraction.ExtractionId, feed.AttemptId),
                (feed.SourceId, ItemUrl("b"), items[1].Extraction.ExtractionId, feed.AttemptId)],
            (await CandidatesAsync()).OrderBy(row => row.Url, StringComparer.Ordinal));
        Assert.Equal(2L, await ScalarAsync("SELECT count(*) FROM statement_analysis WHERE task = 'gate' AND status = 'Pending'"));
        Assert.Equal(2L, await ScalarAsync("SELECT count(*) FROM document_extractions"));
    }

    [Fact]
    public async Task AReplayOfTheSameFeedAttemptNeverAddsCandidatesBeyondTheLimit()
    {
        var feed = await seeder.ImportAsync("feed", FeedUrl, CandidateJobKind.None);
        var items = Items(feed, "a", "b", "c");
        await extractions.SaveFeedItemsAsync(items, 2, CancellationToken.None);

        var replayed = await extractions.SaveFeedItemsAsync(items, 2, CancellationToken.None);

        Assert.Equal(0, replayed);
        Assert.Equal(2L, await ScalarAsync("SELECT count(*) FROM statement_candidates"));
    }

    [Fact]
    public async Task AFeedItemForALinkThatAPageJobAlreadyHoldsAddsNeitherACandidateNorAnExtraction()
    {
        var page = await seeder.ImportAsync("Page text.", ItemUrl("a"), CandidateJobKind.DiscoveryAdmitted);
        await extractions.SaveAsync(new DocumentExtraction(page, "p", "Page text."), CancellationToken.None);
        var feed = await seeder.ImportAsync("feed", FeedUrl, CandidateJobKind.None);

        var registered = await extractions.SaveFeedItemsAsync(Items(feed, "a"), 5, CancellationToken.None);

        Assert.Equal(0, registered);
        Assert.Equal([page.AttemptId], (await CandidatesAsync()).Select(row => row.Attempt));
        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM document_extractions"));
    }

    [Fact]
    public async Task APageJobForALinkThatAFeedItemAlreadyHoldsKeepsTheFeedItemTextAsTheFirstText()
    {
        var feed = await seeder.ImportAsync("feed", FeedUrl, CandidateJobKind.None);
        var items = Items(feed, "a");
        await extractions.SaveFeedItemsAsync(items, 5, CancellationToken.None);
        var page = await seeder.ImportAsync("Page text.", ItemUrl("a"), CandidateJobKind.DiscoveryAdmitted);

        await extractions.SaveAsync(new DocumentExtraction(page, "p", "Page text."), CancellationToken.None);

        Assert.Equal([(feed.SourceId, ItemUrl("a"), items[0].Extraction.ExtractionId, feed.AttemptId)], await CandidatesAsync());
        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM statement_analysis"));
    }

    private static string ItemUrl(string name) => "https://example.test/pages/" + name;

    private static FeedItemExtraction[] Items(CapturedAttemptResult feed, params string[] names) =>
        [.. names.Select((name, part) => new FeedItemExtraction(ItemUrl(name),
            new DocumentExtraction(feed, "feed-v1", "Link: " + ItemUrl(name) + "\n\nText of " + name, part: part)))];

    private Task SeedVoteRowAsync(string candidateId, int roll, int congress, int billNumber) => ExecuteAsync(connectionString, $"""
        SET session_replication_role = replica;
        INSERT INTO vote_candidates (candidate_id, source_id, chamber, congress, session_number, roll_call_number, update_date_utc_ticks, bill_type,
            bill_number, detail_job_id, members_job_id, detail_extraction_id, members_extraction_id, configuration_revision_id, created_at_utc_ticks)
        VALUES ('{candidateId}', 'house', 'House', {congress}, 1, {roll}, 1, 'hconres', {billNumber}, 'detail-{roll}', 'members-{roll}',
                '{new string('c', 64)}', '{new string('c', 64)}', '{new string('c', 64)}', 1);
        INSERT INTO vote_analysis (candidate_id, task, status, fence, attempts, reserved_tokens, created_at_utc_ticks)
        VALUES ('{candidateId}', 'vote-draft', 'Pending', 0, 0, 0, 1)
        """);

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
