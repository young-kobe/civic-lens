using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using AngleSharp.Html.Parser;
using CivicLens.Application.Collection;
using CivicLens.Collection.Contracts;
using CivicLens.Core.Collection;
using CivicLens.Core.Documents;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Documents;
using CivicLens.Tests.Infrastructure.Collection;
using Npgsql;
using static CivicLens.Tests.Fixtures.CollectionFixtures;

namespace CivicLens.Tests.Host;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class DocumentViewerTests(PostgresCollection postgres) : IAsyncLifetime
{
    private readonly string schema = "viewer_" + Guid.NewGuid().ToString("N");
    private readonly HttpClient client = new() { Timeout = TimeSpan.FromSeconds(15) };
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
        start.ArgumentList.Add("viewer");
        start.ArgumentList.Add(port.ToString(System.Globalization.CultureInfo.InvariantCulture));
        start.Environment["CIVIC_LENS_DATABASE"] = connectionString;
        start.Environment["ASPNETCORE_URLS"] = "http://0.0.0.0:1";
        start.Environment["Kestrel__Endpoints__Injected__Url"] = "http://0.0.0.0:1";
        process = Process.Start(start)!;
        output = process.StandardOutput.ReadToEndAsync();
        error = process.StandardError.ReadToEndAsync();
        client.BaseAddress = new Uri($"http://127.0.0.1:{port}");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        while (true)
        {
            if (process.HasExited) throw new InvalidOperationException($"Viewer exited: {await output} {await error}");
            try
            {
                using var response = await client.GetAsync("/", deadline.Token);
                if (response.IsSuccessStatusCode) break;
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
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"DROP SCHEMA IF EXISTS {schema} CASCADE", connection);
        await command.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task RetainedTextAndCitationsAreEncodedAndReadsDoNotWrite()
    {
        const string text = "\nFirst line\r\n<script>alert('source')</script>\nA \U0001F600 quotation.\u0080\u0085\u0091";
        var extraction = await SaveExtractionAsync(text);
        using var response = await client.GetAsync("/Documents/Extraction?extractionId=" + extraction.ExtractionId);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("<script>alert", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.Contains(text, WebUtility.HtmlDecode(html));
        var document = await new HtmlParser().ParseDocumentAsync(html);
        Assert.Equal(text, document.QuerySelector(".evidence-text")!.TextContent);
        Assert.Empty(document.QuerySelectorAll("script"));
        Assert.Contains(extraction.ExtractionId, html);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Contains("default-src", response.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Contains("form-action 'self'", response.Headers.GetValues("Content-Security-Policy").Single());
        using var stylesheet = await client.GetAsync("/css/site.css");
        Assert.Equal(HttpStatusCode.OK, stylesheet.StatusCode);
        var start = text.IndexOf("\U0001F600", StringComparison.Ordinal);
        using var citation = await client.GetAsync($"/Documents/Citation?extractionId={extraction.ExtractionId}&start={start}&length=2");
        Assert.Equal(HttpStatusCode.OK, citation.StatusCode);
        Assert.Contains("\U0001F600", WebUtility.HtmlDecode(await citation.Content.ReadAsStringAsync()));
        using var split = await client.GetAsync($"/Documents/Citation?extractionId={extraction.ExtractionId}&start={start}&length=1");
        Assert.Equal(HttpStatusCode.BadRequest, split.StatusCode);
        using var overflow = await client.GetAsync($"/Documents/Citation?extractionId={extraction.ExtractionId}&start=999999999999&length=1");
        Assert.Equal(HttpStatusCode.BadRequest, overflow.StatusCode);
        using var missing = await client.GetAsync("/Documents/Extraction?extractionId=" + new string('f', 64));
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        using var invalidId = await client.GetAsync("/Documents/Extraction?extractionId=" + new string('F', 64));
        Assert.Equal(HttpStatusCode.BadRequest, invalidId.StatusCode);
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT (SELECT count(*) FROM document_extractions), (SELECT count(*) FROM document_comparisons)", connection);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(1L, reader.GetInt64(0));
        Assert.Equal(0L, reader.GetInt64(1));
    }

    [Fact]
    public async Task HistoryAndSavedComparisonRetainEvidenceAndExplicitNonCompleteStates()
    {
        var before = await SaveExtractionAsync("Deadline October 15.\nEligibility unchanged.");
        var after = await SaveExtractionAsync("Deadline October 30.\nEligibility unchanged.");
        var request = Request();
        var failed = Receipt(request) with
        {
            Outcome = CollectionOutcome.Failed,
            Capture = null,
            FailureCode = CollectionFailureCode.HttpError,
            Response = new HttpResponseMetadata { StatusCode = 503, ContentEncodings = [] }
        };
        await attempts.ImportAtomicallyAsync(CollectionAttemptImporter.CreateImport("failed-check", request, failed), default);
        using var history = await client.GetAsync("/Documents/History?sourceId=source&requestedUrl=" + Uri.EscapeDataString(request.Url));
        Assert.Equal(HttpStatusCode.OK, history.StatusCode);
        var html = await history.Content.ReadAsStringAsync();
        Assert.Contains("failed-check", html);
        Assert.Contains(before.ExtractionId, html);
        Assert.Contains(after.ExtractionId, html);
        var changed = await comparisons.SaveAsync(DocumentComparison.Create(before, after), default);
        using var comparison = await client.GetAsync("/Documents/Comparison?comparisonId=" + changed.ComparisonId);
        Assert.Equal(HttpStatusCode.OK, comparison.StatusCode);
        var compared = WebUtility.HtmlDecode(await comparison.Content.ReadAsStringAsync());
        Assert.Contains("Deadline October 15.", compared);
        Assert.Contains("Deadline October 30.", compared);
        Assert.Contains("/Documents/Citation?", compared);
        var upgraded = new DocumentExtraction(after.SourceAttempt, "parser-v2", "normalization-v1", after.Text);
        await extractions.SaveAsync(upgraded, default);
        var incompatible = await comparisons.SaveAsync(DocumentComparison.Create(before, upgraded), default);
        using var incompatibleResponse = await client.GetAsync("/Documents/Comparison?comparisonId=" + incompatible.ComparisonId);
        Assert.Contains("incompatible", (await incompatibleResponse.Content.ReadAsStringAsync()).ToLowerInvariant());
        var longText = await SaveExtractionAsync(string.Join('\n', Enumerable.Repeat("line", 2002)));
        var limited = await comparisons.SaveAsync(DocumentComparison.Create(before, longText), default);
        using var limitedResponse = await client.GetAsync("/Documents/Comparison?comparisonId=" + limited.ComparisonId);
        Assert.Contains("limit", (await limitedResponse.Content.ReadAsStringAsync()).ToLowerInvariant());
    }

    [Fact]
    public async Task CorruptEvidenceFailsWithoutDisclosingProviderDetails()
    {
        var extraction = await SaveExtractionAsync("original text");
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("UPDATE document_extractions SET text = 'corrupted' WHERE extraction_id = @id", connection);
        command.Parameters.AddWithValue("id", extraction.ExtractionId);
        await command.ExecuteNonQueryAsync();
        using var response = await client.GetAsync("/Documents/Extraction?extractionId=" + extraction.ExtractionId);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(connectionString, html);
        Assert.DoesNotContain("InvalidOperationException", html);
        Assert.DoesNotContain("corrupted", html);
        await using var corruptAttempt = new NpgsqlCommand("UPDATE collection_attempts SET final_url = chr(9) WHERE attempt_id = @id", connection);
        corruptAttempt.Parameters.AddWithValue("id", extraction.SourceAttempt.AttemptId);
        await corruptAttempt.ExecuteNonQueryAsync();
        using var history = await client.GetAsync("/Documents/History?sourceId=source&requestedUrl=" + Uri.EscapeDataString(extraction.SourceAttempt.RequestedUrl));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, history.StatusCode);
        Assert.DoesNotContain("ArgumentException", await history.Content.ReadAsStringAsync());
    }

    private async Task<DocumentExtraction> SaveExtractionAsync(string text)
    {
        var request = Request();
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
}
