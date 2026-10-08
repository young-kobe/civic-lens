using System.Diagnostics;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using AngleSharp.Html.Parser;
using AngleSharp.Dom;
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
