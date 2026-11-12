using CivicLens.Application.Collection;
using CivicLens.Application.Collection.FederalRegister;
using CivicLens.Core.Review;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Collection.Jobs;
using CivicLens.Infrastructure.Documents;
using CivicLens.Tests.Fixtures;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.Collection.FederalRegister;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PostgresFederalRegisterTextTests(PostgresCollection postgres) : IAsyncLifetime
{
    private static readonly ReviewActor Owner = new("auth0|owner", ReviewRole.Owner);
    private readonly string captureRoot = Path.Combine(Path.GetTempPath(), "federal-register-text-" + Guid.NewGuid().ToString("N"));
    private string connectionString = null!;
    private PostgresCollectionJobStore store = null!;
    private JobExtractionSeeder extractions = null!;

    public async Task InitializeAsync()
    {
        connectionString = await postgres.CreateMigratedDatabaseAsync();
        store = PostgresCollectionJobStore.FromConnectionString(connectionString, TestCodeVersion.Current);
        var documents = PostgresDocumentExtractionStore.FromConnectionString(connectionString);
        Directory.CreateDirectory(captureRoot);
        var seeder = new StatementCandidateSeeder(PostgresCollectionAttemptStore.FromConnectionString(connectionString), documents,
            connectionString, captureRoot);
        extractions = new JobExtractionSeeder(store, documents, seeder, connectionString, captureRoot);
    }

    public async Task DisposeAsync()
    {
        await postgres.DropDatabaseAsync(connectionString);
        Directory.Delete(captureRoot, recursive: true);
    }

    private const string Proclamation = FederalRegisterFixtures.BidenProclamation;

    /// <summary>Test-shaped pinned URLs for fixtures whose fetch did not pin one. They are not fetched values.</summary>
    private static readonly string PinnedOrder = FederalRegisterFixtures.WithBodyUrl(FederalRegisterFixtures.TrumpOrder,
        FederalRegisterFixtures.TextSourceUrl + "2025/01/28/2025-01900.html");
    private static readonly string PinnedNotice = FederalRegisterFixtures.WithBodyUrl(FederalRegisterFixtures.BidenNotice,
        FederalRegisterFixtures.TextSourceUrl + "2024/11/04/2024-25777.html");

    [Fact]
    public async Task AnOriginalDocumentAdmitsExactlyOneTextJobInTheTransactionThatRecordsItAndAReplayAdmitsNone()
    {
        var admitted = await AdmitDocumentAsync(Proclamation);

        Assert.Single(admitted.Jobs);
        Assert.Equal("federal-register/federal-register-text/text/2024-26714",
            await ScalarAsync("SELECT idempotency_key FROM collection_jobs WHERE idempotency_key LIKE '%/text/%'"));
        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM executive_action_documents"));

        var replay = await AdmitDocumentAsync(Proclamation);

        Assert.Empty(replay.Jobs);
        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM collection_jobs WHERE idempotency_key LIKE '%/text/%'"));
    }

    [Fact]
    public async Task TheTextJobFetchesThePinnedUrlEvenWhenItsPathDateDiffersFromThePublicationDate()
    {
        Assert.Equal(new DateOnly(2024, 11, 13), FederalRegisterFixtures.Document(Proclamation).PublishedOn);
        Assert.Contains("/2024/11/14/", FederalRegisterFixtures.ProclamationBodyUrl, StringComparison.Ordinal);

        var admitted = await AdmitDocumentAsync(Proclamation);

        Assert.Equal(FederalRegisterFixtures.ProclamationBodyUrl, Assert.Single(admitted.Jobs).Url);
        Assert.Equal(FederalRegisterFixtures.ProclamationBodyUrl, await ScalarAsync("SELECT body_html_url FROM executive_action_documents"));
        Assert.Equal(new DateOnly(2024, 11, 13), await ScalarAsync("SELECT published_on FROM executive_action_documents"));
    }

    [Fact]
    public async Task AnOriginalWithNoPinnedUrlIsRecordedWithANullUrlAndAdmitsNoTextJob()
    {
        var admitted = await AdmitDocumentAsync(FederalRegisterFixtures.TrumpOrder);

        Assert.Empty(admitted.Jobs);
        Assert.Equal(DBNull.Value, await ScalarAsync("SELECT body_html_url FROM executive_action_documents"));
        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM collection_jobs WHERE idempotency_key LIKE '%/text/%'"));
    }

    [Fact]
    public async Task APinnedUrlOutsideTheTextPrefixFailsTheDocumentAndRecordsNothing()
    {
        var outside = Proclamation.Replace("/documents/full_text/html/", "/documents/full_text/html-x/", StringComparison.Ordinal);

        await Assert.ThrowsAsync<InvalidDataException>(() => AdmitDocumentAsync(outside));

        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM executive_action_documents"));
        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM collection_jobs WHERE idempotency_key LIKE '%/text/%'"));
    }

    [Fact]
    public async Task ACorrectionAdmitsNoTextJobAndTheOriginalThatFollowsItAdmitsOne()
    {
        var correction = await AdmitDocumentAsync(FederalRegisterFixtures.BidenProclamationCorrection);

        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM collection_jobs WHERE idempotency_key LIKE '%/text/%'"));
        Assert.Equal(FederalRegisterJobKeys.Document(FederalRegisterFixtures.SourceId, "2024-26714"),
            await ScalarAsync("SELECT idempotency_key FROM collection_jobs WHERE idempotency_key LIKE '%/document/2024-26714'"));
        Assert.DoesNotContain(correction.Jobs, job => job.Url.Contains("full_text", StringComparison.Ordinal));

        _ = await AdmitDocumentAsync(Proclamation);
        _ = await AdmitDocumentAsync(FederalRegisterFixtures.BidenProclamationCorrection);

        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM collection_jobs WHERE idempotency_key LIKE '%/text/%'"));
        Assert.Equal("federal-register/federal-register-text/text/2024-26714",
            await ScalarAsync("SELECT idempotency_key FROM collection_jobs WHERE idempotency_key LIKE '%/text/%'"));
    }

    [Fact]
    public async Task WithNoEnabledTextSourceADocumentAdmitsNoTextJob()
    {
        var disabled = FederalRegisterFixtures.Configuration(withTextSource: true);
        disabled = disabled with { Sources = [disabled.Sources[0], disabled.Sources[1] with { Enabled = false }] };

        var admitted = await AdmitDocumentAsync(Proclamation, disabled);

        Assert.Empty(admitted.Jobs);
        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM collection_jobs WHERE idempotency_key LIKE '%/text/%'"));
    }

    [Fact]
    public async Task TheTextIsStoredOnceWithItsCountsAndAReplayAddsNoRow()
    {
        var (jobId, number, extractionId) = await PrepareTextAsync(Proclamation, "short text");
        var evidence = Evidence(number, extractionId, FederalRegisterRenderer.RenderText(number, "short text"));

        await store.AdmitTextAsync(TextSource(), jobId, evidence, default);
        await store.AdmitTextAsync(TextSource(), jobId, evidence, default);

        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM executive_action_texts"));
        Assert.Equal(("2024-26714", extractionId, 10, 10), await RowAsync());
    }

    [Fact]
    public async Task TextOverTheWindowKeepsTheTotalAndTheRowSaysHowMuchWasRead()
    {
        var text = string.Join(' ', Enumerable.Range(1, 20_000).Select(n => $"word{n}"));
        var (jobId, number, extractionId) = await PrepareTextAsync(Proclamation, text);
        var reading = FederalRegisterRenderer.RenderText(number, text);

        await store.AdmitTextAsync(TextSource(), jobId, Evidence(number, extractionId, reading), default);

        var (_, _, read, total) = await RowAsync();
        Assert.Equal((reading.CharsRead, text.Length), (read, total));
        Assert.True(read <= TextWindow.Length && read < total);
    }

    [Fact]
    public async Task AnExtractionOfAnotherJobACorrectionNoticeAndAnotherUrlThanThePinnedOneAreInvalidDataAndStoreNothing()
    {
        var (jobId, number, extractionId) = await PrepareTextAsync(Proclamation, "short text");
        var other = await store.AdmitListPageAsync(DocumentsSource(), new FederalRegisterListPage([new("2025-01901", new DateOnly(2025, 1, 28))]), default);
        var otherJob = other.Jobs.Single().JobId;
        await SucceededJobs.MarkSucceededAsync(connectionString, otherJob);
        var foreign = await extractions.SaveAsync(otherJob, "x", "x", "extraction");
        var reading = FederalRegisterRenderer.RenderText(number, "short text");

        await Assert.ThrowsAsync<InvalidDataException>(() => store.AdmitTextAsync(TextSource(), jobId, Evidence(number, foreign, reading), default));
        await Assert.ThrowsAsync<InvalidDataException>(() => store.AdmitTextAsync(TextSource(), jobId,
            Evidence(number, extractionId, reading) with { BodyUrl = FederalRegisterFixtures.TextSourceUrl + "2024/11/13/2024-26714.html" }, default));
        await Assert.ThrowsAsync<InvalidDataException>(() => store.AdmitTextAsync(TextSource(), jobId,
            Evidence("2025-09999", extractionId, reading), default));

        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM executive_action_texts"));
    }

    [Fact]
    public async Task TheDatabaseRefusesCountsThatBreakTheWindowRule()
    {
        var (jobId, number, extractionId) = await PrepareTextAsync(Proclamation, "short text");
        await store.AdmitTextAsync(TextSource(), jobId, Evidence(number, extractionId, FederalRegisterRenderer.RenderText(number, "short text")), default);

        foreach (var set in new[] { "chars_read = 0", "chars_read = 70000, chars_total = 70000", "chars_read = 5", "chars_total = 3" })
            await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync($"UPDATE executive_action_texts SET {set}"));
    }

    [Fact]
    public async Task TheBackfillAdmitsAtMostTheLimitNewestOriginalsFirstAndARerunAfterTheDrainAdmitsTheRest()
    {
        await AdmitDocumentAsync(PinnedNotice, withText: false);
        await AdmitDocumentAsync(Proclamation, withText: false);
        await AdmitDocumentAsync(PinnedOrder, withText: false);
        await AdmitDocumentAsync(FederalRegisterFixtures.BidenProclamationCorrection, withText: false);
        var backfill = Backfill();

        var first = await backfill.ExecuteAsync(Owner, 2);

        Assert.Equal(new FederalRegisterTextBackfillResult(0, 2, 2, 0), first);
        Assert.Equal(["2024-26714", "2025-01900"], await KeysAsync());
        Assert.Contains(FederalRegisterFixtures.ProclamationBodyUrl, await TextJobUrlsAsync());

        await MarkTextJobsSucceededAsync();
        var second = await backfill.ExecuteAsync(Owner, 2);
        await MarkTextJobsSucceededAsync();
        var third = await backfill.ExecuteAsync(Owner, 2);

        Assert.Equal(new FederalRegisterTextBackfillResult(0, 1, 1, 0), second);
        Assert.Equal(new FederalRegisterTextBackfillResult(0, 0, 0, 0), third);
        Assert.Equal(["2024-25777", "2024-26714", "2025-01900"], await KeysAsync());
    }

    [Fact]
    public async Task TheBackfillRefusesAndAdmitsNothingWhileAFederalRegisterJobIsPending()
    {
        await AdmitDocumentAsync(Proclamation, withText: false);
        _ = await store.AdmitListPageAsync(DocumentsSource(false), new FederalRegisterListPage([new("2025-01901", new DateOnly(2025, 1, 28))]), default);
        var backfill = Backfill();

        var refused = await backfill.ExecuteAsync(Owner, 10);

        Assert.Equal(new FederalRegisterTextBackfillResult(1, 0, 0, 0), refused);
        Assert.Empty(await KeysAsync());

        await SucceededJobs.MarkSucceededAsync(connectionString,
            (string)(await ScalarAsync("SELECT job_id FROM collection_jobs WHERE idempotency_key LIKE '%/document/2025-01901'"))!);
        var drained = await backfill.ExecuteAsync(Owner, 10);

        Assert.Equal(new FederalRegisterTextBackfillResult(0, 1, 1, 0), drained);
    }

    [Fact]
    public async Task TheBackfillCountsOriginalsWithNoPinnedUrlAndSkipsADocumentThatHasItsTextJob()
    {
        var admitted = await AdmitDocumentAsync(Proclamation);
        await MarkTextJobsSucceededAsync();
        await AdmitDocumentAsync(PinnedOrder, withText: false);
        await AdmitDocumentAsync(FederalRegisterFixtures.BidenNotice, withText: false);
        var backfill = Backfill();

        var result = await backfill.ExecuteAsync(Owner, 10);

        Assert.Equal(new FederalRegisterTextBackfillResult(0, 1, 1, 1), result);
        Assert.Equal(["2024-26714", "2025-01900"], await KeysAsync());
        Assert.Single(admitted.Jobs);
    }

    [Fact]
    public async Task TheBackfillRefusesAConfigurationWithNoTextSourceALimitBelowOneAndANonOwner()
    {
        var backfill = Backfill();

        await Assert.ThrowsAsync<ArgumentException>(() => new BackfillFederalRegisterText(store,
            CollectionConfigurationRevision.Create(FederalRegisterFixtures.Configuration())).ExecuteAsync(Owner, 10));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => backfill.ExecuteAsync(Owner, 0));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => backfill.ExecuteAsync(new ReviewActor("auth0|reviewer", ReviewRole.Reviewer), 1));
    }

    private static FederalRegisterTextEvidence Evidence(string number, string extractionId, FederalRegisterTextReading reading) =>
        new(number, FederalRegisterFixtures.ProclamationBodyUrl, reading, extractionId);

    private BackfillFederalRegisterText Backfill() =>
        new(store, CollectionConfigurationRevision.Create(FederalRegisterFixtures.Configuration(withTextSource: true)));

    private async Task MarkTextJobsSucceededAsync()
    {
        foreach (var jobId in await ColumnAsync("SELECT job_id FROM collection_jobs WHERE idempotency_key LIKE '%/text/%' AND state <> 'Succeeded'"))
            await SucceededJobs.MarkSucceededAsync(connectionString, jobId);
    }

    private Task<string[]> TextJobUrlsAsync() =>
        ColumnAsync("SELECT definition_json::jsonb ->> 'url' FROM collection_jobs WHERE idempotency_key LIKE '%/text/%'");

    private static ConfiguredCollectionSource DocumentsSource(bool withText = true) =>
        new(FederalRegisterFixtures.Configuration(withTextSource: withText), FederalRegisterFixtures.SourceId, DateOnly.FromDateTime(DateTime.UtcNow));

    private static ConfiguredCollectionSource TextSource() =>
        new(FederalRegisterFixtures.Configuration(withTextSource: true), FederalRegisterFixtures.TextSourceId, DateOnly.FromDateTime(DateTime.UtcNow));

    private async Task<CivicLens.Application.Collection.Discovery.DiscoveryAdmissionResult> AdmitDocumentAsync(string json, CollectionConfiguration? configuration = null,
        bool withText = true)
    {
        var document = FederalRegisterFixtures.Document(json);
        var source = configuration is null
            ? DocumentsSource(withText)
            : new ConfiguredCollectionSource(configuration, FederalRegisterFixtures.SourceId, DateOnly.FromDateTime(DateTime.UtcNow));
        var jobId = await EnqueueDocumentAsync(document.DocumentNumber, document.PublishedOn);
        var text = FederalRegisterRenderer.Render(document);
        var extractionId = await extractions.SaveAsync(jobId, json, text, FederalRegisterDocumentParser.ExtractionVersion);
        var personId = document.PresidentName.Contains("Trump", StringComparison.Ordinal) ? "donald-trump" : "joe-biden";
        return await store.AdmitDocumentAsync(source, jobId, new FederalRegisterEvidence(document, extractionId, text, personId), default);
    }

    private async Task<string> EnqueueDocumentAsync(string number, DateOnly publishedOn)
    {
        var existing = await ScalarAsync($"SELECT job_id FROM collection_jobs WHERE idempotency_key LIKE '%/document/{number}'");
        if (existing is string known) return known;
        _ = await store.AdmitListPageAsync(DocumentsSource(false), new FederalRegisterListPage([new FederalRegisterListItem(number, publishedOn)]), default);
        var jobId = (string)(await ScalarAsync($"SELECT job_id FROM collection_jobs WHERE idempotency_key LIKE '%/document/{number}'"))!;
        await SucceededJobs.MarkSucceededAsync(connectionString, jobId);
        return jobId;
    }

    private async Task<(string TextJobId, string Number, string ExtractionId)> PrepareTextAsync(string json, string text)
    {
        _ = await AdmitDocumentAsync(json);
        var number = FederalRegisterFixtures.NumberOf(json);
        var jobId = (string)(await ScalarAsync($"SELECT job_id FROM collection_jobs WHERE idempotency_key LIKE '%/text/{number}'"))!;
        await SucceededJobs.MarkSucceededAsync(connectionString, jobId);
        var reading = FederalRegisterRenderer.RenderText(number, text);
        var extractionId = await extractions.SaveAsync(jobId, FederalRegisterFixtures.OrderBodyHtml, reading.Text,
            "federal-register-text-v1+html-text-v1");
        return (jobId, number, extractionId);
    }

    private async Task<(string Number, string ExtractionId, int CharsRead, int CharsTotal)> RowAsync()
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT document_number, extraction_id, chars_read, chars_total FROM executive_action_texts", connection);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (reader.GetString(0), reader.GetString(1), reader.GetInt32(2), reader.GetInt32(3));
    }

    private Task<string[]> KeysAsync() =>
        ColumnAsync("SELECT split_part(idempotency_key, '/', 4) FROM collection_jobs WHERE idempotency_key LIKE '%/text/%' ORDER BY 1");

    private async Task<string[]> ColumnAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var values = new List<string>();
        while (await reader.ReadAsync()) values.Add(reader.GetString(0));
        return [.. values];
    }

    private async Task<object?> ScalarAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return await command.ExecuteScalarAsync();
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
