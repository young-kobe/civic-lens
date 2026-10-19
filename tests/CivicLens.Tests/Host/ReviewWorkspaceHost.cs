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
using CivicLens.Application.Issues;
using CivicLens.Core.Review;
using CivicLens.Collection.Contracts;
using CivicLens.Core.Collection;
using CivicLens.Core.Documents;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Collection.Jobs;
using CivicLens.Infrastructure.Collection.Processing;
using CivicLens.Infrastructure.Documents;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Infrastructure.Collection;
using static CivicLens.Tests.Fixtures.CollectionFixtures;

namespace CivicLens.Tests.Host;

/// <summary>Runs the review workspace as a real process against an isolated Postgres database, with offline sign-in cookies.</summary>
public sealed class ReviewWorkspaceHost(PostgresCollection postgres)
{
    public HttpClient Client { get; } = new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { Timeout = TimeSpan.FromSeconds(15) };
    public string KeyDirectory { get; } = Path.Combine(Path.GetTempPath(), "civic-review-keys-" + Guid.NewGuid().ToString("N"));
    public string ReleaseDirectory => Path.Combine(KeyDirectory, "releases");
    private Process? process;
    private Task<string>? output;
    private readonly StringBuilder errors = new();
    public string ConnectionString { get; private set; } = null!;
    public PostgresCollectionAttemptStore Attempts { get; private set; } = null!;
    public PostgresDocumentExtractionStore Extractions { get; private set; } = null!;
    public PostgresDocumentComparisonStore Comparisons { get; private set; } = null!;
    public string OfficialId { get; init; } = "person";
    public string OfficialName { get; init; } = "Test Official";
    public bool Publishing { get; init; } = true;
    public IssueConfiguration? Issues { get; init; }

    public async Task StartAsync()
    {
        ConnectionString = await postgres.CreateMigratedDatabaseAsync();
        Attempts = PostgresCollectionAttemptStore.FromConnectionString(ConnectionString);
        Extractions = PostgresDocumentExtractionStore.FromConnectionString(ConnectionString);
        Comparisons = PostgresDocumentComparisonStore.FromConnectionString(ConnectionString);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        var assembly = Path.Combine(RepositoryRoot.FullName, "src", "CivicLens.Host", "bin", configuration, "net10.0", "CivicLens.Host.dll");
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
        foreach (var name in HostProcess.DraftingVariables) start.Environment.Remove(name);
        start.Environment["CIVIC_LENS_DATABASE"] = ConnectionString;
        start.Environment["ASPNETCORE_URLS"] = "http://0.0.0.0:1";
        start.Environment["Kestrel__Endpoints__Injected__Url"] = "http://0.0.0.0:1";
        start.Environment["CIVIC_LENS_REVIEW_ORIGIN"] = "https://review.example.test";
        start.Environment["CIVIC_LENS_AUTH0_AUTHORITY"] = "https://tenant.example.test";
        start.Environment["CIVIC_LENS_AUTH0_CLIENT_ID"] = "offline-test-client";
        start.Environment["CIVIC_LENS_AUTH0_CLIENT_SECRET"] = "offline-test-secret";
        start.Environment["CIVIC_LENS_REVIEW_OWNER"] = "auth0|owner";
        start.Environment["CIVIC_LENS_REVIEW_REVIEWERS"] = "auth0|friend";
        start.Environment["CIVIC_LENS_REVIEW_KEY_DIRECTORY"] = KeyDirectory;
        Directory.CreateDirectory(KeyDirectory);
        var collectionPath = Path.Combine(KeyDirectory, "sources.json");
        await File.WriteAllTextAsync(collectionPath, $$"""
            {"version":2,"people":[{"id":{{JsonSerializer.Serialize(OfficialId)}},"name":{{JsonSerializer.Serialize(OfficialName)}}}],
             "documentProfiles":[{"id":"main","selector":"main"}],
             "sources":[{"id":"source","coverage":[{"personId":{{JsonSerializer.Serialize(OfficialId)}}}],
               "url":"https://example.test/pages/a","allowedOrigin":"https://example.test",
               "allowedPathPrefix":"/pages","documentProfileId":"main"}]}
            """);
        start.Environment["CIVIC_LENS_COLLECTION_CONFIG"] = collectionPath;
        if (Publishing) start.Environment["CIVIC_LENS_RELEASE_DIRECTORY"] = ReleaseDirectory;
        if (Issues is not null)
        {
            var issuesPath = Path.Combine(KeyDirectory, "issues.json");
            await File.WriteAllTextAsync(issuesPath, JsonSerializer.Serialize(Issues, CollectionProtocol.JsonOptions));
            start.Environment["CIVIC_LENS_ISSUES_CONFIG"] = issuesPath;
        }
        process = Process.Start(start)!;
        output = process.StandardOutput.ReadToEndAsync();
        process.ErrorDataReceived += (_, line) =>
        {
            lock (errors) errors.AppendLine(line.Data);
        };
        process.BeginErrorReadLine();
        Client.BaseAddress = new Uri($"http://127.0.0.1:{port}");
        Client.DefaultRequestHeaders.Host = "review.example.test";
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        while (true)
        {
            if (process.HasExited) throw new InvalidOperationException($"Review workspace exited: {await output} {HostErrors}");
            try
            {
                using var response = await Client.GetAsync("/access-denied", deadline.Token);
                if (response.StatusCode == HttpStatusCode.Forbidden) break;
            }
            catch (HttpRequestException) { }
            await Task.Delay(100, deadline.Token);
        }
    }

    public async Task StopAsync()
    {
        Client.Dispose();
        if (process is not null)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            if (output is not null) await output;
            process.Dispose();
        }
        if (Directory.Exists(KeyDirectory)) Directory.Delete(KeyDirectory, recursive: true);
        await postgres.DropDatabaseAsync(ConnectionString);
    }

    public string HostErrors
    {
        get
        {
            lock (errors) return errors.ToString();
        }
    }

    public async Task<IDocument> GetDocumentAsync(string url)
    {
        using var response = await GetWithHostErrorsAsync(url);
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"GET {url} returned {(int)response.StatusCode}. Host stderr: {HostErrors}");
        if (response.Headers.TryGetValues("Set-Cookie", out var cookies))
        {
            var retained = Client.DefaultRequestHeaders.GetValues("Cookie").Single().Split("; ").ToList();
            foreach (var cookie in cookies)
            {
                var pair = cookie.Split(';')[0];
                var name = pair.Split('=')[0];
                retained.RemoveAll(value => value.StartsWith(name + "=", StringComparison.Ordinal));
                retained.Add(pair);
            }
            Client.DefaultRequestHeaders.Remove("Cookie");
            Client.DefaultRequestHeaders.Add("Cookie", string.Join("; ", retained));
        }
        return await new HtmlParser().ParseDocumentAsync(await response.Content.ReadAsStringAsync());
    }

    private async Task<HttpResponseMessage> GetWithHostErrorsAsync(string url)
    {
        try
        {
            return await Client.GetAsync(url);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            throw new InvalidOperationException($"GET {url} failed. Host stderr: {HostErrors}", exception);
        }
    }

    public static FormUrlEncodedContent Form(IElement form) => new(Fields(form));

    public static List<KeyValuePair<string, string>> Fields(IElement form) => form.QuerySelectorAll("input[name], textarea[name]")
        .Where(element => element.GetAttribute("type") is not ("checkbox" or "radio") || element.HasAttribute("checked"))
        .Select(element => new KeyValuePair<string, string>(element.GetAttribute("name")!,
            element.LocalName == "textarea" ? element.TextContent : element.GetAttribute("value") ?? "")).ToList();

    public static FormUrlEncodedContent TriageDecision(IDocument page, string buttonName, string? note = null)
    {
        var fields = Fields(page.QuerySelector("#decide")!);
        fields.Add(new(buttonName, page.QuerySelector($"button[form='decide'][name='{buttonName}']")!.GetAttribute("value")!));
        if (note is not null) fields.Add(new(page.QuerySelector("textarea[form='decide']")!.GetAttribute("name")!, note));
        return new(fields);
    }

    public static void Set(List<KeyValuePair<string, string>> fields, string name, string value)
    {
        fields.RemoveAll(field => field.Key == name);
        fields.Add(new(name, value));
    }

    public string CreateCookie(string subject, TimeSpan? lifetime = null)
    {
        // Only this test harness holds the isolated signing keys. Production has no test-login mode.
        var provider = DataProtectionProvider.Create(new DirectoryInfo(KeyDirectory),
            options => options.SetApplicationName("CivicLens.Review"));
        var protector = provider.CreateProtector("Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationMiddleware", "Cookies", "v2");
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", subject)], "Cookies")),
            new AuthenticationProperties { IssuedUtc = DateTimeOffset.UtcNow, ExpiresUtc = DateTimeOffset.UtcNow.Add(lifetime ?? TimeSpan.FromMinutes(10)) },
            CookieAuthenticationDefaults.AuthenticationScheme);
        return "__Host-CivicLens.Review=" + new TicketDataFormat(protector).Protect(ticket);
    }

    public async Task<DocumentExtraction> SaveExtractionAsync(string text, CollectionRequest? requestOverride = null)
    {
        var request = requestOverride ?? Request();
        var bytes = Encoding.UTF8.GetBytes(text);
        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var receipt = Receipt(request) with
        {
            BytesReceived = bytes.Length,
            Capture = new CaptureArtifact { Sha256 = hash, RelativePath = hash + ".gz", ByteLength = bytes.Length }
        };
        var imported = await Attempts.ImportAtomicallyAsync(CollectionAttemptImporter.CreateImport(
            Guid.NewGuid().ToString("N"), request, receipt), default);
        var extraction = new DocumentExtraction((CapturedAttemptResult)imported.AttemptResult,
            "parser-v1", "normalization-v1", text);
        return await Extractions.SaveAsync(extraction, default);
    }

    public async Task<CollectionImportDecision> SaveImportedAttemptAsync(CollectionRequest request, string attemptId, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var receipt = Receipt(request) with
        {
            BytesReceived = bytes.Length,
            Capture = new CaptureArtifact { Sha256 = hash, RelativePath = hash + ".gz", ByteLength = bytes.Length }
        };
        return await Attempts.ImportAtomicallyAsync(CollectionAttemptImporter.CreateImport(attemptId, request, receipt), default);
    }

    public sealed class CapturedFixtureCollector(string html) : ICollectorProcess
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
