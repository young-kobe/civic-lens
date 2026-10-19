using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CivicLens.Application.Collection;
using CivicLens.Application.Configuration;
using CivicLens.Application.Collection.Discovery;
using CivicLens.Collection.Contracts;
using CivicLens.Collector.Http;
using CivicLens.Infrastructure.Collection;
using CivicLens.Tests.Infrastructure.Collection;
using Npgsql;

namespace CivicLens.Tests.Host;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class DatabaseCommandTests(PostgresCollection postgres) : IAsyncLifetime
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "civic-cli-" + Guid.NewGuid().ToString("N"));
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource shutdown = new();
    private string connectionString = null!;
    private Task server = Task.CompletedTask;
    private int statusCode = 200;
    private string? feedBody;

    public async Task InitializeAsync()
    {
        connectionString = await postgres.CreateMigratedDatabaseAsync();
        Directory.CreateDirectory(directory);
        listener.Start();
        var origin = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}";
        var configuration = new CollectionConfiguration
        {
            People = [new PersonConfiguration { Id = "person", Name = "Fixture Official" }],
            Sources = [new WatchedSourceConfiguration
            {
                Id = "source", Coverage = [new SourceCoverageConfiguration { PersonId = "person" }], Url = origin + "/page", AllowedOrigin = origin,
                AllowedPathPrefix = "/page", MinDelayMilliseconds = 0, TimeoutSeconds = 5
            }]
        };
        await File.WriteAllTextAsync(Path.Combine(directory, "config.json"),
            JsonSerializer.Serialize(configuration, ConfigurationJson.Options));
        server = ServeAsync();
    }

    public async Task DisposeAsync()
    {
        shutdown.Cancel();
        listener.Stop();
        try { await server; }
        catch (Exception exception) when (exception is OperationCanceledException or SocketException) { }
        shutdown.Dispose();
        Directory.Delete(directory, recursive: true);
        await postgres.DropDatabaseAsync(connectionString);
    }

    [Theory]
    [InlineData(CollectionMode.Feed)]
    [InlineData(CollectionMode.Html)]
    public async Task DiscoveryRecoveryAdmissionReplayAndArticleCollectionWorkThroughCli(CollectionMode mode)
    {
        var configPath = Path.Combine(directory, "config.json");
        var config = JsonSerializer.Deserialize<CollectionConfiguration>(await File.ReadAllTextAsync(configPath),
            ConfigurationJson.Options)!;
        config = config with
        {
            Version = 2,
            Sources = [config.Sources[0] with
            {
                Coverage = [new SourceCoverageConfiguration
                { PersonId = "person", StartsOn = new DateOnly(2020, 1, 1), EndsBefore = new DateOnly(2021, 1, 1) }],
                Mode = mode, AdmissionPolicy = new DiscoveryAdmissionPolicy { MaxJobs = 1 }
            }]
        };
        await File.WriteAllTextAsync(configPath, JsonSerializer.Serialize(config, ConfigurationJson.Options));
        var entries = Enumerable.Range(0, 100).Select(index =>
            $"<item><link>/page/article-{index}?value={new string('a', 750)}</link></item>");
        feedBody = mode == CollectionMode.Feed
            ? "<rss version=\"2.0\"><channel>" + string.Concat(entries) + "</channel></rss>"
            : "<html><body>" + string.Concat(Enumerable.Range(0, 100).Select(index =>
                $"<a href=\"/page/article-{index}?value={new string('a', 750)}\">Article</a>")) + "</body></html>";
        Assert.True(feedBody.Length > 65_536);
        var attemptId = await SpoolReceiptAsync(new DateOnly(2020, 1, 1));
        var root = Path.Combine(directory, "captures");
        var replay = await HostProcess.RunWithCollectorHostAsync(connectionString, "/no-collector-host",
            "receipts", "replay", root, attemptId);
        Assert.True(replay.ExitCode == 0, replay.Error);
        var inspected = await HostProcess.RunAsync(connectionString, "discovery", "get", attemptId);
        Assert.True(inspected.ExitCode == 0, inspected.Error);
        using var discovery = JsonDocument.Parse(inspected.Output);
        Assert.Equal("parsed", discovery.RootElement.GetProperty("status").GetString());
        Assert.Equal(100, discovery.RootElement.GetProperty("urls").GetArrayLength());
        var inactive = await HostProcess.RunAsync(connectionString, "discovery", "admit", configPath, "source", attemptId,
            "batch-1", "--as-of", "2021-01-01");
        Assert.Equal(2, inactive.ExitCode);
        var args = new[] { "discovery", "admit", configPath, "source", attemptId, "batch-1", "--as-of", "2020-01-01" };
        var admitted = await HostProcess.RunAsync(connectionString, args);
        Assert.True(admitted.ExitCode == 0, admitted.Error);
        var repeated = await HostProcess.RunAsync(connectionString, args);
        Assert.Equal(admitted.Output, repeated.Output);
        var replayAfterExpiry = await HostProcess.RunAsync(connectionString, args[..^2]);
        Assert.True(replayAfterExpiry.ExitCode == 0, replayAfterExpiry.Error);
        Assert.Equal(admitted.Output, replayAfterExpiry.Output);
        Assert.Equal(2, (await HostProcess.RunAsync(connectionString, "discovery", "admit", configPath, "source", attemptId, "new-after-expiry")).ExitCode);
        using var admission = JsonDocument.Parse(admitted.Output);
        var jobId = Assert.Single(admission.RootElement.GetProperty("jobs").EnumerateArray()).GetProperty("jobId").GetString()!;
        var inspectedJob = await HostProcess.RunAsync(connectionString, "jobs", "get", jobId);
        using var job = JsonDocument.Parse(inspectedJob.Output);
        Assert.Equal("2020-01-01", job.RootElement.GetProperty("definition").GetProperty("coverageAsOf").GetString());
        Assert.Equal(64, job.RootElement.GetProperty("definition").GetProperty("configurationRevision").GetProperty("id").GetString()!.Length);
        await ExpireCoverageAsync(configPath);
        var run = await RunWorkerOnceAsync(configPath, root);
        Assert.True(run.ExitCode == 0, run.Error + run.Output);
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var count = new NpgsqlCommand("SELECT count(*) FROM collection_attempts", connection);
        Assert.Equal(2L, await count.ExecuteScalarAsync());
    }

    [Fact]
    public async Task ManagedJobRunsOnceAndARepeatedWorkerPassDoesNotCollectAgain()
    {
        var configPath = Path.Combine(directory, "config.json");
        var arguments = new[] { "jobs", "enqueue", configPath, "source", "check-1" };
        var enqueued = await HostProcess.RunAsync(connectionString, arguments);
        Assert.Equal(0, enqueued.ExitCode);
        using var definition = JsonDocument.Parse(enqueued.Output);
        var id = definition.RootElement.GetProperty("jobId").GetString()!;
        var duplicate = await HostProcess.RunAsync(connectionString, arguments);
        using var duplicateOutput = JsonDocument.Parse(duplicate.Output);
        Assert.Equal(id, duplicateOutput.RootElement.GetProperty("jobId").GetString());
        await ExpireCoverageAsync(configPath);
        var run = await RunWorkerOnceAsync(configPath, Path.Combine(directory, "captures"));
        Assert.True(run.ExitCode == 0, run.Error + run.Output);
        shutdown.Cancel();
        listener.Stop();
        var again = await RunWorkerOnceAsync(configPath, Path.Combine(directory, "captures"));
        Assert.Equal(0, again.ExitCode);
        var inspected = await HostProcess.RunAsync(connectionString, "jobs", "get", id);
        using var inspection = JsonDocument.Parse(inspected.Output);
        Assert.Equal("succeeded", inspection.RootElement.GetProperty("state").GetString());
        Assert.Single(inspection.RootElement.GetProperty("attempts").EnumerateArray());
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var count = new NpgsqlCommand("SELECT count(*) FROM collection_attempts", connection);
        Assert.Equal(1L, await count.ExecuteScalarAsync());
    }

    [Fact]
    public async Task DirectCollectionUsesExplicitCoverageDate()
    {
        var path = Path.Combine(directory, "config.json");
        var configuration = JsonSerializer.Deserialize<CollectionConfiguration>(await File.ReadAllTextAsync(path), ConfigurationJson.Options)!;
        configuration = configuration with
        {
            Version = 2,
            Sources = [configuration.Sources[0] with
            {
                Coverage = [new SourceCoverageConfiguration
                { PersonId = "person", StartsOn = new DateOnly(2020, 1, 1), EndsBefore = new DateOnly(2021, 1, 1) }]
            }]
        };
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(configuration, ConfigurationJson.Options));
        var inactive = await CollectAsync("2021-01-01");
        Assert.Equal(2, inactive.ExitCode);
        Assert.Empty(inactive.Output);
        var active = await CollectAsync("2020-01-01");
        Assert.True(active.ExitCode == 0, active.Error);
        using var output = JsonDocument.Parse(active.Output);
        Assert.Equal("captured", output.RootElement.GetProperty("outcome").GetString());
    }

    [Fact]
    public async Task DatedJobAdmissionPinsRevisionAndDateAcrossConfigChange()
    {
        var configPath = Path.Combine(directory, "config.json");
        var config = JsonNode.Parse(await File.ReadAllTextAsync(configPath))!;
        var source = config["sources"]![0]!.AsObject();
        source["coverage"] = new JsonArray(new JsonObject
        {
            ["personId"] = "person",
            ["startsOn"] = "2020-07-01",
            ["endsBefore"] = "2020-08-01"
        });
        await File.WriteAllTextAsync(configPath, config.ToJsonString());
        Assert.Equal(0, (await HostProcess.RunAsync(null, "validate", configPath)).ExitCode);
        var inactive = await HostProcess.RunAsync(connectionString, "jobs", "enqueue", configPath, "source", "dated",
            "--as-of", "2020-08-01");
        Assert.Equal(2, inactive.ExitCode);
        var arguments = new[] { "jobs", "enqueue", configPath, "source", "dated", "--as-of", "2020-07-01" };
        var enqueued = await HostProcess.RunAsync(connectionString, arguments);
        Assert.True(enqueued.ExitCode == 0, enqueued.Error);
        using var job = JsonDocument.Parse(enqueued.Output);
        var id = job.RootElement.GetProperty("jobId").GetString()!;
        var definition = job.RootElement.GetProperty("definition");
        Assert.Equal("2020-07-01", definition.GetProperty("coverageAsOf").GetString());
        var revision = definition.GetProperty("configurationRevision").GetProperty("id").GetString();
        var replay = await HostProcess.RunAsync(connectionString, arguments[..^2]);
        Assert.True(replay.ExitCode == 0, replay.Error);
        using var replayed = JsonDocument.Parse(replay.Output);
        Assert.Equal(id, replayed.RootElement.GetProperty("jobId").GetString());
        Assert.Equal("2020-07-01", replayed.RootElement.GetProperty("definition").GetProperty("coverageAsOf").GetString());
        Assert.Equal(2, (await HostProcess.RunAsync(connectionString, "jobs", "enqueue", configPath, "source", "new-after-expiry")).ExitCode);
        config["people"]![0]!["name"] = "Corrected name";
        await File.WriteAllTextAsync(configPath, config.ToJsonString());
        Assert.Equal(2, (await HostProcess.RunAsync(connectionString, arguments)).ExitCode);
        await ExpireCoverageAsync(configPath);
        var run = await RunWorkerOnceAsync(configPath, Path.Combine(directory, "captures"));
        Assert.True(run.ExitCode == 0, run.Error + run.Output);
        var inspected = await HostProcess.RunAsync(connectionString, "jobs", "get", id);
        using var stored = JsonDocument.Parse(inspected.Output);
        Assert.Equal(revision, stored.RootElement.GetProperty("definition").GetProperty("configurationRevision").GetProperty("id").GetString());
    }

    [Fact]
    public async Task ReplayNeverMigratesAndExplicitRepeatableMigrationLetsTheReceiptReplay()
    {
        var empty = await postgres.CreateEmptyDatabaseAsync();
        try
        {
            var id = await SpoolReceiptAsync();
            var result = await HostProcess.RunAsync(empty, "receipts", "replay", Path.Combine(directory, "captures"), id);
            Assert.Equal(1, result.ExitCode);
            Assert.Empty(result.Output);
            Assert.Contains("Persistence was not confirmed", result.Error);
            Assert.Single(Directory.GetFiles(Path.Combine(directory, "captures"), "*.gz"));
            await using (var connection = new NpgsqlConnection(empty))
            {
                await connection.OpenAsync();
                await using var command = new NpgsqlCommand("SELECT to_regclass('collection_attempts') IS NULL", connection);
                Assert.Equal(true, await command.ExecuteScalarAsync());
            }

            Assert.Equal(0, (await HostProcess.RunAsync(empty, "db", "migrate")).ExitCode);
            Assert.Equal(0, (await HostProcess.RunAsync(empty, "db", "migrate")).ExitCode);
            var replayed = await HostProcess.RunAsync(empty, "receipts", "replay", Path.Combine(directory, "captures"), "--all");
            Assert.True(replayed.ExitCode == 0, replayed.Error);
            Assert.Contains("\"status\":\"importedAndRemoved\"", replayed.Output, StringComparison.Ordinal);
        }
        finally
        {
            await postgres.DropDatabaseAsync(empty);
        }
    }

    [Theory]
    [InlineData(7)]
    [InlineData(9)]
    public async Task FailedImportCanReplayAfterRelocationWithoutConfigurationOrCollector(int protocolVersion)
    {
        var id = await SpoolReceiptAsync();
        var original = Path.Combine(directory, "captures");
        var listed = await HostProcess.RunAsync(null, "receipts", "list", original);
        Assert.Equal(0, listed.ExitCode);
        Assert.Equal(id, Assert.Single(JsonSerializer.Deserialize<string[]>(listed.Output)!));
        if (protocolVersion == 7)
        {
            var path = Path.Combine(original, ".pending", id + ".json");
            var envelope = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
            var request = envelope["request"]!.AsObject();
            request["version"] = protocolVersion;
            var receipt = envelope["receipt"]!.AsObject();
            receipt["version"] = protocolVersion;
            await File.WriteAllTextAsync(path, envelope.ToJsonString());
        }

        var relocated = Path.Combine(directory, "relocated-captures");
        Directory.Move(original, relocated);
        File.Delete(Path.Combine(directory, "config.json"));
        shutdown.Cancel();
        listener.Stop();

        var replayed = await HostProcess.RunWithCollectorHostAsync(connectionString, "/no-collector-host",
            "receipts", "replay", relocated, id);
        Assert.Equal(0, replayed.ExitCode);
        using var output = JsonDocument.Parse(replayed.Output);
        Assert.Equal(id, output.RootElement.GetProperty("attemptId").GetString());
        Assert.True(output.RootElement.GetProperty("handoffRemoved").GetBoolean());
        Assert.Equal("newAttempt", output.RootElement.GetProperty("importDisposition").GetString());
        var empty = await HostProcess.RunAsync(null, "receipts", "list", relocated);
        Assert.Equal("[]", empty.Output.Trim());

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var count = new NpgsqlCommand("SELECT count(*) FROM collection_attempts WHERE attempt_id = @id", connection);
        count.Parameters.AddWithValue("id", id);
        Assert.Equal(1L, await count.ExecuteScalarAsync());
    }

    [Fact]
    public async Task BatchRecoveryRetainsInvalidEntryAndImportsOtherPendingReceipt()
    {
        await SpoolReceiptAsync();
        var root = Path.Combine(directory, "captures");
        var invalid = Path.Combine(root, ".pending", "000-invalid.json");
        await File.WriteAllTextAsync(invalid, "{broken");

        var batch = await HostProcess.RunAsync(connectionString, "receipts", "replay", root, "--all");
        Assert.Equal(1, batch.ExitCode);
        var lines = batch.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        Assert.Contains(lines, line => line.Contains("\"status\":\"invalidHandoff\"", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("\"status\":\"importedAndRemoved\"", StringComparison.Ordinal));
        Assert.True(File.Exists(invalid));
        Assert.Single(Directory.GetFiles(Path.Combine(root, ".pending"), "*.json"));
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var count = new NpgsqlCommand("SELECT count(*) FROM collection_attempts", connection);
        Assert.Equal(1L, await count.ExecuteScalarAsync());
    }

    [Fact]
    public async Task CollectAndValidationRemainDatabaseFree()
    {
        var validation = await HostProcess.RunAsync(null, "validate", Path.Combine(directory, "config.json"));
        Assert.Equal(0, validation.ExitCode);
        var traced = await CollectAsync();
        Assert.Equal(0, traced.ExitCode);
        using var receipt = JsonDocument.Parse(traced.Output);
        Assert.Equal("captured", receipt.RootElement.GetProperty("outcome").GetString());
        Assert.False(receipt.RootElement.TryGetProperty("attemptId", out _));
    }

    private Task<(int ExitCode, string Output, string Error)> CollectAsync(string? asOf = null)
    {
        string[] arguments = ["collect", Path.Combine(directory, "config.json"), "source",
            typeof(HttpCollector).Assembly.Location, Path.Combine(directory, "captures")];
        if (asOf is not null) arguments = [.. arguments, "--as-of", asOf];
        return HostProcess.RunAsync(null, arguments);
    }

    private static async Task ExpireCoverageAsync(string configPath)
    {
        var config = JsonNode.Parse(await File.ReadAllTextAsync(configPath))!;
        config["sources"]![0]!["coverage"] = new JsonArray(new JsonObject
        {
            ["personId"] = "person",
            ["startsOn"] = "1990-01-01",
            ["endsBefore"] = "1991-01-01"
        });
        await File.WriteAllTextAsync(configPath, config.ToJsonString());
    }

    private Task<(int ExitCode, string Output, string Error)> RunWorkerOnceAsync(string configPath, string captures) =>
        HostProcess.RunWithEnvironmentAsync(connectionString, null,
            new Dictionary<string, string> { ["CIVIC_LENS_COLLECTION_CONFIG"] = configPath },
            "worker", typeof(HttpCollector).Assembly.Location, captures, "--once");

    private async Task<string> SpoolReceiptAsync(DateOnly? asOf = null)
    {
        var configuration = JsonSerializer.Deserialize<CollectionConfiguration>(
            await File.ReadAllTextAsync(Path.Combine(directory, "config.json")), ConfigurationJson.Options)!;
        var root = Path.Combine(directory, "captures");
        var attempt = new PreparedCollectionAttempt(new ConfiguredCollectionSource(configuration, "source",
            asOf ?? DateOnly.FromDateTime(DateTime.UtcNow)), root);
        var receipt = await new CollectorProcess(typeof(HttpCollector).Assembly.Location).RunAsync(attempt.Request, CancellationToken.None);
        var id = Guid.NewGuid().ToString("N");
        await new FileCollectionReceiptHandoffStore(Path.GetFullPath(root)).SaveAsync(
            new PendingCollectionHandoff(PendingCollectionHandoff.CurrentVersion, id, attempt.Request, receipt), CancellationToken.None);
        return id;
    }

    private async Task ServeAsync()
    {
        while (!shutdown.IsCancellationRequested)
        {
            using var client = await listener.AcceptTcpClientAsync(shutdown.Token);
            await using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
            var request = await reader.ReadLineAsync(shutdown.Token);
            while (await reader.ReadLineAsync(shutdown.Token) is { Length: > 0 }) { }
            var robots = request!.Contains(" /robots.txt ", StringComparison.Ordinal);
            var status = robots ? 404 : statusCode;
            var body = !robots && status == 200
                ? feedBody is not null && request!.Contains(" /page ", StringComparison.Ordinal) ? feedBody : "Verified CLI evidence."
                : "";
            var response = $"HTTP/1.1 {status} Fixture\r\nContent-Length: {body.Length}\r\nConnection: close\r\nRetry-After: 10\r\n\r\n{body}";
            await stream.WriteAsync(Encoding.ASCII.GetBytes(response), shutdown.Token);
        }
    }
}
