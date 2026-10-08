using System.Diagnostics;
using System.IO.Compression;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AngleSharp.Html.Parser;
using AngleSharp.Dom;
using CivicLens.Application.Collection;
using CivicLens.Application.Collection.Jobs;
using CivicLens.Application.Collection.Processing;
using CivicLens.Application.Documents;
using CivicLens.Core.Review;
using CivicLens.Collection.Contracts;
using CivicLens.Core.Collection;
using CivicLens.Core.Documents;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Collection.Jobs;
using CivicLens.Infrastructure.Collection.Processing;
using CivicLens.Infrastructure.Documents;
using CivicLens.Tests.Infrastructure.Collection;
using Npgsql;
using static CivicLens.Tests.Fixtures.CollectionFixtures;

namespace CivicLens.Tests.Host;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class ReviewWorkspaceTests(PostgresCollection postgres) : IAsyncLifetime
{
    private readonly string schema = "review_web_" + Guid.NewGuid().ToString("N");
    private readonly HttpClient client = new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { Timeout = TimeSpan.FromSeconds(15) };
    private readonly string keyDirectory = Path.Combine(Path.GetTempPath(), "civic-review-keys-" + Guid.NewGuid().ToString("N"));
    private Process? process;
    private Task<string>? output;
    private Task<string>? error;
    private string connectionString = null!;
    private PostgresCollectionAttemptStore attempts = null!;
    private PostgresDocumentExtractionStore extractions = null!;
    private PostgresDocumentComparisonStore comparisons = null!;

    public async Task InitializeAsync()
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"CREATE SCHEMA {schema}", connection);
        await command.ExecuteNonQueryAsync();
        connectionString = new NpgsqlConnectionStringBuilder(postgres.ConnectionString) { SearchPath = schema, Pooling = false }.ConnectionString;
        attempts = PostgresCollectionAttemptStore.FromConnectionString(connectionString);
        extractions = PostgresDocumentExtractionStore.FromConnectionString(connectionString);
        comparisons = PostgresDocumentComparisonStore.FromConnectionString(connectionString);
        await attempts.MigrateAsync();
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "CivicLens.slnx"))) root = root.Parent;
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        var assembly = Path.Combine(root!.FullName, "src", "CivicLens.Host", "bin", configuration, "net10.0", "CivicLens.Host.dll");
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = Path.GetTempPath()
        };
        start.ArgumentList.Add(assembly);
        start.ArgumentList.Add("review");
        start.ArgumentList.Add(port.ToString(System.Globalization.CultureInfo.InvariantCulture));
        start.Environment["CIVIC_LENS_DATABASE"] = connectionString;
        start.Environment["ASPNETCORE_URLS"] = "http://0.0.0.0:1";
        start.Environment["Kestrel__Endpoints__Injected__Url"] = "http://0.0.0.0:1";
        start.Environment["CIVIC_LENS_REVIEW_ORIGIN"] = "https://review.example.test";
        start.Environment["CIVIC_LENS_AUTH0_AUTHORITY"] = "https://tenant.example.test";
        start.Environment["CIVIC_LENS_AUTH0_CLIENT_ID"] = "offline-test-client";
        start.Environment["CIVIC_LENS_AUTH0_CLIENT_SECRET"] = "offline-test-secret";
        start.Environment["CIVIC_LENS_REVIEW_OWNER"] = "auth0|owner";
        start.Environment["CIVIC_LENS_REVIEW_REVIEWERS"] = "auth0|friend";
        start.Environment["CIVIC_LENS_REVIEW_KEY_DIRECTORY"] = keyDirectory;
        Directory.CreateDirectory(keyDirectory);
        var collectionPath = Path.Combine(keyDirectory, "sources.json");
        await File.WriteAllTextAsync(collectionPath, """
            {"version":2,"people":[{"id":"person","name":"Test Official"}],
             "documentProfiles":[{"id":"main","selector":"main"}],
             "sources":[{"id":"source","coverage":[{"personId":"person"}],
               "url":"https://example.test/pages/a","allowedOrigin":"https://example.test",
               "allowedPathPrefix":"/pages","documentProfileId":"main"}]}
            """);
        start.Environment["CIVIC_LENS_COLLECTION_CONFIG"] = collectionPath;
        start.Environment["CIVIC_LENS_CAPTURE_DIRECTORY"] = keyDirectory;
        process = Process.Start(start)!;
        output = process.StandardOutput.ReadToEndAsync();
        error = process.StandardError.ReadToEndAsync();
        client.BaseAddress = new Uri($"http://127.0.0.1:{port}");
        client.DefaultRequestHeaders.Host = "review.example.test";
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        while (true)
        {
            if (process.HasExited) throw new InvalidOperationException($"Viewer exited: {await output} {await error}");
            try
            {
                using var response = await client.GetAsync("/access-denied", deadline.Token);
                if (response.StatusCode == HttpStatusCode.Forbidden) break;
            }
            catch (HttpRequestException) { }
            await Task.Delay(100, deadline.Token);
        }
    }

    public async Task DisposeAsync()
    {
        client.Dispose();
        if (process is not null)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            if (output is not null) await output;
            if (error is not null) await error;
            process.Dispose();
        }
        if (Directory.Exists(keyDirectory)) Directory.Delete(keyDirectory, recursive: true);
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"DROP SCHEMA IF EXISTS {schema} CASCADE", connection);
        await command.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task AuthenticatedWorkspaceListsRealEvidenceAndRejectsPostsWithoutAntiforgeryAsync()
    {
        var before = await SaveExtractionAsync("Deadline: October 15.\n");
        var after = await SaveExtractionAsync("Deadline: October 30.\n");
        var comparison = await comparisons.SaveAsync(DocumentComparison.Create(before, after), default);
        client.DefaultRequestHeaders.Add("Cookie", CreateCookie("auth0|owner"));
        using var page = await client.GetAsync("/Review");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var html = await page.Content.ReadAsStringAsync();
        Assert.Contains(comparison.ComparisonId, html);
        Assert.Contains("__RequestVerificationToken", html);
        Assert.True(page.Headers.CacheControl?.NoStore);
        using var forged = await client.PostAsync("/Review?handler=Create", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["ComparisonId"] = comparison.ComparisonId,
            ["IdempotencyKey"] = Guid.NewGuid().ToString("N")
        }));
        Assert.Equal(HttpStatusCode.BadRequest, forged.StatusCode);
        using var evidence = await client.GetAsync("/Documents/Extraction?extractionId=" + before.ExtractionId);
        Assert.Equal(HttpStatusCode.OK, evidence.StatusCode);
        using var logout = await client.GetAsync("/Review/Logout");
        Assert.Equal(HttpStatusCode.MethodNotAllowed, logout.StatusCode);
        using var invalidHost = new HttpRequestMessage(HttpMethod.Get, "/Review");
        invalidHost.Headers.Host = "attacker.example.test";
        using var refused = await client.SendAsync(invalidHost);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
    }

    [Fact]
    public async Task StaleDraftPreservesWordingButDisablesApprovalUntilSavedOrReloadedAsync()
    {
        var before = await SaveExtractionAsync("Before policy.\n");
        var after = await SaveExtractionAsync("After policy.\n");
        await comparisons.SaveAsync(DocumentComparison.Create(before, after), default);
        client.DefaultRequestHeaders.Add("Cookie", CreateCookie("auth0|owner"));
        var queue = await GetDocumentAsync("/Review");
        var create = queue.QuerySelector("form[action*='Create']")!;
        using var created = await client.PostAsync(create.GetAttribute("action"), Form(create));
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        var editorUrl = created.Headers.Location!.ToString();
        var editor = await GetDocumentAsync(editorUrl);
        Assert.Equal("Untitled public record", editor.QuerySelector("#saved-approval-target h2")!.TextContent);
        var save = editor.QuerySelector("form[data-unsaved-form]")!;
        var fields = Fields(save);
        Set(fields, "Input.Headline", "Saved headline");
        Set(fields, "Input.Summary", "Supported summary");
        Set(fields, "Input.Institution", "Example Office");
        using var saved = await client.PostAsync(save.GetAttribute("action"), new FormUrlEncodedContent(fields));
        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
        Set(fields, "Input.Headline", "Recovered unsaved headline");
        Set(fields, "idempotencyKey", Guid.NewGuid().ToString("N"));
        using var stale = await client.PostAsync(save.GetAttribute("action"), new FormUrlEncodedContent(fields));
        var conflict = await new HtmlParser().ParseDocumentAsync(await stale.Content.ReadAsStringAsync());
        Assert.Contains("Recovered unsaved headline", conflict.QuerySelector("#Input_Headline")!.GetAttribute("value"));
        Assert.True(conflict.QuerySelector("#decision-controls")!.HasAttribute("disabled"));
        Assert.Equal("true", conflict.QuerySelector("[data-unsaved-form]")!.GetAttribute("data-recovered-unsaved"));
        editor = await GetDocumentAsync(editorUrl);
        Assert.False(editor.QuerySelector("#decision-controls")!.HasAttribute("disabled"));
        var decision = editor.QuerySelector("#decision-form")!;
        var decisionFields = Fields(decision);
        Set(decisionFields, "kind", "RequestChanges");
        // Missing required reason must redisplay the saved revision, not blank form defaults.
        using var invalidDecision = await client.PostAsync(decision.GetAttribute("action"), new FormUrlEncodedContent(decisionFields));
        var invalid = await new HtmlParser().ParseDocumentAsync(await invalidDecision.Content.ReadAsStringAsync());
        Assert.Equal("Saved headline", invalid.QuerySelector("#Input_Headline")!.GetAttribute("value"));
        Assert.True(invalid.QuerySelector("#decision-controls")!.HasAttribute("disabled"));
        var evidenceLink = editor.QuerySelector("a[href='/Evidence']");
        Assert.NotNull(evidenceLink);
        using var evidence = await client.GetAsync("/Evidence");
        Assert.Equal(HttpStatusCode.OK, evidence.StatusCode);
    }

    [Fact]
    public async Task SavedApprovalTargetKeepsSavedAccountAndCitationsSeparateFromRecoveredUnsavedInputAsync()
    {
        const string untrustedSource = "<img src=x onerror=alert(1)>";
        var before = await SaveExtractionAsync("Earlier wording.\n");
        var after = await SaveExtractionAsync(untrustedSource + "\n");
        var comparison = await comparisons.SaveAsync(DocumentComparison.Create(before, after), default);
        client.DefaultRequestHeaders.Add("Cookie", CreateCookie("auth0|owner"));

        var comparisonPage = await GetDocumentAsync("/Documents/Comparison?comparisonId=" + comparison.ComparisonId);
        Assert.NotNull(comparisonPage.QuerySelector(".review-shell"));
        Assert.Equal(2, comparisonPage.QuerySelectorAll(".diff-passage").Length);
        var create = comparisonPage.QuerySelector("form[action*='Create']")!;
        using var missingToken = await client.PostAsync(create.GetAttribute("action"), new FormUrlEncodedContent(
            Fields(create).Where(field => field.Key != "__RequestVerificationToken")));
        Assert.Equal(HttpStatusCode.BadRequest, missingToken.StatusCode);
        using var created = await client.PostAsync(create.GetAttribute("action"), Form(create));
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        using var replay = await client.PostAsync(create.GetAttribute("action"), Form(create));
        Assert.Equal(created.Headers.Location, replay.Headers.Location);
        var editorUrl = created.Headers.Location!.ToString();
        var editor = await GetDocumentAsync(editorUrl);
        var save = editor.QuerySelector("form[data-unsaved-form]")!;
        var fields = Fields(save);
        Set(fields, "Input.Headline", "Saved public account");
        Set(fields, "Input.Summary", "A saved summary for review.");
        Set(fields, "Input.Institution", "Example Office");
        fields.RemoveAll(field => field.Key == "Input.EvidenceSelection");
        var evidenceOptions = editor.QuerySelectorAll("input[name='Input.EvidenceSelection']");
        var earlierEvidence = evidenceOptions.Single(input => input.ParentElement!.TextContent.Contains("Earlier wording", StringComparison.Ordinal));
        var laterEvidence = evidenceOptions.Single(input => input.ParentElement!.TextContent.Contains(untrustedSource, StringComparison.Ordinal));
        fields.Add(new("Input.EvidenceSelection", earlierEvidence.GetAttribute("value")!));
        fields.Add(new("Input.EvidenceSelection", laterEvidence.GetAttribute("value")!));

        using var saved = await client.PostAsync(save.GetAttribute("action"), new FormUrlEncodedContent(fields));
        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
        editor = await GetDocumentAsync(editorUrl);
        var savedTarget = editor.QuerySelector("#saved-approval-target")!;
        var savedCitations = editor.QuerySelector("#saved-citation-title")!.ParentElement!;
        Assert.Contains("Saved public account", savedTarget.TextContent, StringComparison.Ordinal);
        Assert.Contains("A saved summary for review.", savedTarget.TextContent, StringComparison.Ordinal);
        Assert.Contains("later version", savedCitations.TextContent, StringComparison.Ordinal);
        Assert.Contains("earlier version", savedCitations.TextContent, StringComparison.Ordinal);
        Assert.Contains("Earlier wording", editor.QuerySelector(".review-evidence-column")!.TextContent, StringComparison.Ordinal);
        Assert.Contains("&lt;img src=x onerror=alert(1)&gt;", savedCitations.InnerHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("<img", savedCitations.InnerHtml, StringComparison.Ordinal);
        var citationLinks = savedCitations.QuerySelectorAll("a[href*='/Documents/Citation']");
        Assert.Contains(citationLinks, link => link.GetAttribute("href")!.Contains(before.ExtractionId, StringComparison.Ordinal));
        Assert.Contains(citationLinks, link => link.GetAttribute("href")!.Contains(after.ExtractionId, StringComparison.Ordinal));
        Assert.Equal("false", editor.QuerySelector("#unsaved-record-preview")!.GetAttribute("data-recovered-unsaved"));
        Assert.Contains("Software checks", editor.QuerySelector("#decision-title")!.ParentElement!.TextContent, StringComparison.Ordinal);
        Assert.Contains("You must judge", editor.QuerySelector("#decision-title")!.ParentElement!.TextContent, StringComparison.Ordinal);
        Assert.Contains("keeps this account private", editor.QuerySelector(".review-approval-target")!.TextContent, StringComparison.Ordinal);
        Assert.Contains("publishing is a separate future process", editor.QuerySelector(".review-approval-target")!.TextContent, StringComparison.Ordinal);

        // Submit the pre-save form again to simulate a stale editor after its first save.
        Set(fields, "Input.Headline", "Recovered unsaved public account");
        Set(fields, "idempotencyKey", Guid.NewGuid().ToString("N"));
        using var stale = await client.PostAsync(save.GetAttribute("action"), new FormUrlEncodedContent(fields));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var conflict = await new HtmlParser().ParseDocumentAsync(await stale.Content.ReadAsStringAsync());
        Assert.Equal("Recovered unsaved public account", conflict.QuerySelector("#Input_Headline")!.GetAttribute("value"));
        Assert.Equal("true", conflict.QuerySelector("#unsaved-record-preview")!.GetAttribute("data-recovered-unsaved"));
        var conflictSavedTarget = conflict.QuerySelector("#saved-approval-target")!;
        Assert.Contains("Saved public account", conflictSavedTarget.TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain("Recovered unsaved public account", conflictSavedTarget.TextContent, StringComparison.Ordinal);
        Assert.Contains(after.ExtractionId, conflict.QuerySelector("#saved-citation-title")!.ParentElement!.InnerHtml, StringComparison.Ordinal);
        Assert.True(conflict.QuerySelector("#decision-controls")!.HasAttribute("disabled"));
    }

    [Fact]
    public async Task SourceControlsRequireOwnerAndCsrfAndEnqueueIdempotently()
    {
        client.DefaultRequestHeaders.Add("Cookie", CreateCookie("auth0|friend"));
        using var denied = await client.GetAsync("/Review/Sources");
        Assert.Equal(HttpStatusCode.Redirect, denied.StatusCode);
        Assert.Contains("access-denied", denied.Headers.Location!.ToString());
        using var deniedActivity = await client.GetAsync("/Review/Sources?handler=Activity");
        Assert.Equal(HttpStatusCode.Redirect, deniedActivity.StatusCode);
        client.DefaultRequestHeaders.Remove("Cookie");
        client.DefaultRequestHeaders.Add("Cookie", CreateCookie("auth0|owner"));
        var page = await GetDocumentAsync("/Review/Sources");
        Assert.Contains("Test Official", page.Body!.TextContent);
        var form = page.QuerySelector("form[action*='Collect']")!;
        var fields = Fields(form);
        var withoutToken = fields.Where(item => item.Key != "__RequestVerificationToken");
        using var forged = await client.PostAsync(form.GetAttribute("action"), new FormUrlEncodedContent(withoutToken));
        Assert.Equal(HttpStatusCode.BadRequest, forged.StatusCode);
        using var queued = await client.PostAsync(form.GetAttribute("action"), new FormUrlEncodedContent(fields));
        using var repeated = await client.PostAsync(form.GetAttribute("action"), new FormUrlEncodedContent(fields));
        Assert.Equal(HttpStatusCode.Redirect, queued.StatusCode);
        Assert.Equal(queued.Headers.Location, repeated.Headers.Location);
        var store = PostgresCollectionJobStore.FromConnectionString(connectionString);
        var job = Assert.Single(await store.ListAsync(50, default));
        Assert.Empty(job.Attempts);
        using var activity = await client.GetAsync("/Review/Sources?handler=Activity");
        Assert.Equal(HttpStatusCode.OK, activity.StatusCode);
        Assert.Contains("connect-src 'self'", activity.Headers.GetValues("Content-Security-Policy").Single());
        using var activityJson = JsonDocument.Parse(await activity.Content.ReadAsStringAsync());
        var reportedJob = activityJson.RootElement.GetProperty("jobs").EnumerateArray().Single();
        Assert.Equal(job.JobId, reportedJob.GetProperty("jobId").GetString());
        Assert.Equal("Queued", reportedJob.GetProperty("label").GetString());
        Assert.True(reportedJob.GetProperty("canStop").GetBoolean());
        var detail = await GetDocumentAsync("/Review/Sources?jobId=" + job.JobId);
        Assert.Contains("This check is waiting to start", detail.Body!.TextContent);
        Assert.Null(detail.QuerySelector("form[action*='Recollect']"));
        var cancel = detail.QuerySelector("form[action*='Cancel']")!;
        using var cancelled = await client.PostAsync(cancel.GetAttribute("action"), Form(cancel));
        Assert.Equal(HttpStatusCode.Redirect, cancelled.StatusCode);
        Assert.Equal(CivicLens.Application.Collection.Jobs.CollectionJobState.Cancelled, (await store.GetAsync(job.JobId, default))!.State);
    }

    [Fact]
    public async Task CheckAutomaticallyPreparesEvidenceAndSendsLaterChangesToReviewAsync()
    {
        client.DefaultRequestHeaders.Add("Cookie", CreateCookie("auth0|owner"));
        var jobs = PostgresCollectionJobStore.FromConnectionString(connectionString);
        var processing = PostgresEvidenceProcessingStore.FromConnectionString(connectionString);
        var collector = new CapturedFixtureCollector("<html><body><main>Deadline: October 15.</main></body></html>");
        var runner = new RunCollectionJob(jobs, attempts, new FileCollectionReceiptHandoffStore(keyDirectory),
            new CaptureArtifactVerifier(), collector);
        var worker = new EvidenceProcessingWorker(jobs, attempts, jobs, processing,
            new ExtractDocument(attempts, new CaptureDocumentTextExtractor(), extractions),
            new GetDocumentHistory(PostgresDocumentHistoryStore.FromConnectionString(connectionString)), new CompareDocuments(extractions, comparisons));

        async Task<EvidenceProcessingRecord> CheckAsync()
        {
            var page = await GetDocumentAsync("/Review/Sources");
            var check = page.QuerySelector("form[action*='Collect']")!;
            Assert.Equal("Check", check.QuerySelector("button")!.TextContent.Trim());
            using var queued = await client.PostAsync(check.GetAttribute("action"), Form(check));
            Assert.Equal(HttpStatusCode.Redirect, queued.StatusCode);
            var job = (await jobs.ListAsync(50, default))[0];
            for (var pass = 0; pass < 24; pass++)
            {
                _ = await runner.ExecuteAsync(job.JobId, keyDirectory, TimeSpan.FromSeconds(60));
                _ = await worker.ExecuteAsync(keyDirectory, 20, TimeSpan.FromSeconds(60), default);
                var progress = (await processing.GetByJobIdsAsync([job.JobId], default))
                    .SingleOrDefault(record => record.AttemptId != "prepare");
                if (progress?.Status is EvidenceProcessingStatus.Succeeded or EvidenceProcessingStatus.Blocked or EvidenceProcessingStatus.Failed)
                    return progress;
                await Task.Delay(250);
            }
            throw new InvalidOperationException("The bounded source check did not complete.");
        }

        var baseline = await CheckAsync();
        Assert.Equal(EvidenceProcessingOutcome.Baseline, baseline.Outcome);
        var firstQueue = await GetDocumentAsync("/Review");
        Assert.Null(firstQueue.QuerySelector("form[action*='Create']"));
        collector.Html = "<html><body><main>Deadline: October 30.</main></body></html>";
        var changed = await CheckAsync();
        Assert.Equal(EvidenceProcessingOutcome.Changed, changed.Outcome);
        Assert.NotNull(changed.ComparisonId);
        var sources = await GetDocumentAsync("/Review/Sources");
        Assert.Contains("Ready for review", sources.Body!.TextContent);
        var review = sources.QuerySelector($"form input[name='comparisonId'][value='{changed.ComparisonId}']")!.ParentElement!;
        using var created = await client.PostAsync(review.GetAttribute("action"), Form(review));
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        Assert.StartsWith("/Review/", created.Headers.Location!.ToString(), StringComparison.Ordinal);
        var editor = await GetDocumentAsync(created.Headers.Location.ToString());
        Assert.Contains("October 15", editor.Body!.TextContent);
        Assert.Contains("October 30", editor.Body.TextContent);
        // Replaying a finished processing pass preserves exactly one comparison and evidence outcome.
        _ = await worker.ExecuteAsync(keyDirectory, 20, TimeSpan.FromSeconds(60), default);
        var repeated = (await processing.GetByJobIdsAsync([changed.JobId], default)).Single(record => record.AttemptId == changed.AttemptId);
        Assert.Equal(changed.ComparisonId, repeated.ComparisonId);
        var unchanged = await CheckAsync();
        Assert.Equal(EvidenceProcessingOutcome.Unchanged, unchanged.Outcome);
        Assert.NotNull(unchanged.ComparisonId);
        var unchangedComparison = await comparisons.GetAsync(unchanged.ComparisonId, default);
        Assert.Empty(unchangedComparison!.Hunks);
        var finalQueue = await GetDocumentAsync("/Review");
        Assert.DoesNotContain(unchanged.ComparisonId, finalQueue.Body!.InnerHtml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RecoveredImportedEvidenceWithoutReceiptStillShowsExtractionAndRecollectControls()
    {
        client.DefaultRequestHeaders.Add("Cookie", CreateCookie("auth0|owner"));
        var sourcePage = await GetDocumentAsync("/Review/Sources");
        var collect = sourcePage.QuerySelector("form[action*='Collect']")!;
        var values = Fields(collect);
        Set(values, "requestKey", Guid.NewGuid().ToString("N"));
        using var queued = await client.PostAsync(collect.GetAttribute("action"), new FormUrlEncodedContent(values));
        var jobs = PostgresCollectionJobStore.FromConnectionString(connectionString);
        var jobId = (await jobs.ListAsync(50, default))[0].JobId;
        var job = (await jobs.GetAsync(jobId, default))!;
        const string attemptId = "aabbccddeeff00112233445566778899";
        var request = job.Definition.CreateRequest(attemptId, keyDirectory);
        var imported = await SaveImportedAttemptAsync(request, attemptId, "Retained article text.\n");
        var started = DateTimeOffset.UtcNow.AddMinutes(-1).UtcDateTime.Ticks;
        var completed = DateTimeOffset.UtcNow.UtcDateTime.Ticks;
        var resolution = JsonSerializer.Serialize(new CollectionAttemptResolution(
            CollectionJobAttemptOutcome.Succeeded, null, "EvidenceAlreadyImported"), CollectionProtocol.JsonOptions);
        var requestJson = JsonSerializer.Serialize(request, CollectionProtocol.JsonOptions);
        await using (var connection = new NpgsqlConnection(connectionString))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("""
                INSERT INTO collection_job_attempts (attempt_id, job_id, sequence, request_json, started_at, resolution_json, completed_at)
                VALUES (@attempt, @job, 1, @request, @started, @resolution, @completed);
                UPDATE collection_jobs SET state = 'Succeeded' WHERE job_id = @job;
                """, connection);
            command.Parameters.AddWithValue("attempt", attemptId);
            command.Parameters.AddWithValue("job", jobId);
            command.Parameters.AddWithValue("request", requestJson);
            command.Parameters.AddWithValue("started", started);
            command.Parameters.AddWithValue("resolution", resolution);
            command.Parameters.AddWithValue("completed", completed);
            await command.ExecuteNonQueryAsync();
        }

        using var response = await client.GetAsync("/Review/Sources?jobId=" + jobId);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var document = await new HtmlParser().ParseDocumentAsync(await response.Content.ReadAsStringAsync());
        Assert.Contains("Imported evidence is retained; the transport receipt is unavailable.", document.Body!.TextContent);
        Assert.Contains(imported.AttemptResult.ObservedAt.ToString("yyyy-MM-dd HH:mm:ss 'UTC'"), document.Body.TextContent);
        Assert.Contains(document.QuerySelectorAll("form[action*='Extract']"), form =>
            form.QuerySelector("input[name='attemptId']")?.GetAttribute("value") == attemptId);
        Assert.NotNull(document.QuerySelector("form[action*='Recollect']"));
    }

    [Fact]
    public async Task CorruptPersistedJobDefinitionReturnsSanitizedServiceUnavailable()
    {
        client.DefaultRequestHeaders.Add("Cookie", CreateCookie("auth0|owner"));
        var sourcePage = await GetDocumentAsync("/Review/Sources");
        var form = sourcePage.QuerySelector("form[action*='Collect']")!;
        var fields = Fields(form);
        Set(fields, "requestKey", Guid.NewGuid().ToString("N"));
        using var queued = await client.PostAsync(form.GetAttribute("action"), new FormUrlEncodedContent(fields));
        var jobStore = PostgresCollectionJobStore.FromConnectionString(connectionString);
        var jobId = (await jobStore.ListAsync(50, default))[0].JobId;
        var savedJob = (await jobStore.GetAsync(jobId, default))!;
        var invalidDefinition = JsonSerializer.Serialize(savedJob.Definition with { MaxRequests = 0 }, CollectionProtocol.JsonOptions);
        await using (var connection = new NpgsqlConnection(connectionString))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("UPDATE collection_jobs SET definition_json = @definition WHERE job_id = @job", connection);
            command.Parameters.AddWithValue("definition", invalidDefinition);
            command.Parameters.AddWithValue("job", jobId);
            await command.ExecuteNonQueryAsync();
        }

        using var response = await client.GetAsync("/Review/Sources?jobId=" + jobId);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("The operation could not be confirmed.", body);
        Assert.DoesNotContain("JsonException", body);
        Assert.DoesNotContain("Stored job definition", body);
    }

    [Fact]
    public async Task PreparationRejectsOtherDocumentsAndReversedObservations()
    {
        var configuration = JsonSerializer.Deserialize<CollectionConfiguration>(
            await File.ReadAllTextAsync(Path.Combine(keyDirectory, "sources.json")), CollectionProtocol.JsonOptions)!;
        var store = PostgresCollectionJobStore.FromConnectionString(connectionString);
        var workspace = new CollectionWorkspace(configuration, keyDirectory, store, store,
            new ExtractDocument(attempts, new CaptureDocumentTextExtractor(), extractions), extractions,
            new CompareDocuments(extractions, comparisons), new GetDocumentHistory(PostgresDocumentHistoryStore.FromConnectionString(connectionString)), attempts);
        var actor = new ReviewActor("auth0|owner", ReviewRole.Owner);
        var job = await workspace.EnqueueAsync(actor, "source", Guid.NewGuid().ToString("N"), default);
        var before = await SaveExtractionAsync("Earlier wording.");
        var after = await SaveExtractionAsync("Later wording.");
        var other = await SaveExtractionAsync("Different document.", Request() with { Url = "https://example.test/pages/b" });
        await Assert.ThrowsAsync<ArgumentException>(() => workspace.CompareAsync(actor, job.JobId, after.ExtractionId, before.ExtractionId, default));
        await Assert.ThrowsAsync<ArgumentException>(() => workspace.CompareAsync(actor, job.JobId, before.ExtractionId, other.ExtractionId, default));
        await Assert.ThrowsAsync<ArgumentException>(() => workspace.ExtractAsync(actor, job.JobId, before.SourceAttempt.AttemptId, default));
        var comparison = await workspace.CompareAsync(actor, job.JobId, before.ExtractionId, after.ExtractionId, default);
        Assert.Equal(DocumentComparisonStatus.Complete, comparison.Status);
        Assert.NotEmpty(comparison.Hunks);
    }

    private async Task<IDocument> GetDocumentAsync(string url)
    {
        using var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        if (response.Headers.TryGetValues("Set-Cookie", out var cookies))
        {
            var retained = client.DefaultRequestHeaders.GetValues("Cookie").Single().Split("; ").ToList();
            foreach (var cookie in cookies)
            {
                var pair = cookie.Split(';')[0];
                var name = pair.Split('=')[0];
                retained.RemoveAll(value => value.StartsWith(name + "=", StringComparison.Ordinal));
                retained.Add(pair);
            }
            client.DefaultRequestHeaders.Remove("Cookie");
            client.DefaultRequestHeaders.Add("Cookie", string.Join("; ", retained));
        }
        return await new HtmlParser().ParseDocumentAsync(await response.Content.ReadAsStringAsync());
    }

    private static FormUrlEncodedContent Form(IElement form) => new(Fields(form));

    private static List<KeyValuePair<string, string>> Fields(IElement form) => form.QuerySelectorAll("input[name], textarea[name]")
        .Where(element => element.GetAttribute("type") != "checkbox" || element.HasAttribute("checked"))
        .Select(element => new KeyValuePair<string, string>(element.GetAttribute("name")!,
            element.LocalName == "textarea" ? element.TextContent : element.GetAttribute("value") ?? "")).ToList();

    private static void Set(List<KeyValuePair<string, string>> fields, string name, string value)
    {
        fields.RemoveAll(field => field.Key == name);
        fields.Add(new(name, value));
    }

    private string CreateCookie(string subject)
    {
        // Only this test harness holds the isolated signing keys. Production has no test-login mode.
        var provider = DataProtectionProvider.Create(new DirectoryInfo(keyDirectory),
            options => options.SetApplicationName("CivicLens.Review"));
        var protector = provider.CreateProtector("Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationMiddleware", "Cookies", "v2");
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", subject)], "Cookies")),
            new AuthenticationProperties { IssuedUtc = DateTimeOffset.UtcNow, ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(10) },
            CookieAuthenticationDefaults.AuthenticationScheme);
        return "__Host-CivicLens.Review=" + new TicketDataFormat(protector).Protect(ticket);
    }

    private async Task<DocumentExtraction> SaveExtractionAsync(string text, CollectionRequest? requestOverride = null)
    {
        var request = requestOverride ?? Request();
        var bytes = Encoding.UTF8.GetBytes(text);
        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var receipt = Receipt(request) with
        {
            BytesReceived = bytes.Length,
            Capture = new CaptureArtifact { Sha256 = hash, RelativePath = hash + ".gz", ByteLength = bytes.Length }
        };
        var imported = await attempts.ImportAtomicallyAsync(CollectionAttemptImporter.CreateImport(
            Guid.NewGuid().ToString("N"), request, receipt), default);
        var extraction = new DocumentExtraction((CapturedAttemptResult)imported.AttemptResult,
            "parser-v1", "normalization-v1", text);
        return await extractions.SaveAsync(extraction, default);
    }

    private async Task<CollectionImportDecision> SaveImportedAttemptAsync(CollectionRequest request, string attemptId, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var receipt = Receipt(request) with
        {
            BytesReceived = bytes.Length,
            Capture = new CaptureArtifact { Sha256 = hash, RelativePath = hash + ".gz", ByteLength = bytes.Length }
        };
        return await attempts.ImportAtomicallyAsync(CollectionAttemptImporter.CreateImport(attemptId, request, receipt), default);
    }

    private sealed class CapturedFixtureCollector(string html) : ICollectorProcess
    {
        public string Html { get; set; } = html;

        public async Task<CollectionResult> RunAsync(CollectionRequest request, CancellationToken cancellationToken)
        {
            var bytes = Encoding.UTF8.GetBytes(Html);
            var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
            await using (var file = File.Create(Path.Combine(request.ArtifactDirectory, hash + ".gz")))
            await using (var gzip = new GZipStream(file, CompressionLevel.Fastest))
                await gzip.WriteAsync(bytes, cancellationToken);
            return Receipt(request) with
            {
                BytesReceived = bytes.Length,
                Response = new HttpResponseMetadata { StatusCode = 200, ContentType = "text/html; charset=utf-8", ContentEncodings = [] },
                Capture = new CaptureArtifact { Sha256 = hash, RelativePath = hash + ".gz", ByteLength = bytes.Length }
            };
        }
    }
}
