using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using CivicLens.Application.Collection;
using CivicLens.Collection.Contracts;
using CivicLens.Collector.Http;
using CivicLens.Tests.Infrastructure.Collection;
using Npgsql;

namespace CivicLens.Tests.Host;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class DatabaseCommandTests(PostgresCollection postgres) : IAsyncLifetime
{
    private readonly string schema = "cli_" + Guid.NewGuid().ToString("N");
    private readonly string directory = Path.Combine(Path.GetTempPath(), "civic-cli-" + Guid.NewGuid().ToString("N"));
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource shutdown = new();
    private string connectionString = null!;
    private Task server = Task.CompletedTask;
    private int statusCode = 200;

    public async Task InitializeAsync()
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"CREATE SCHEMA {schema}", connection);
        await command.ExecuteNonQueryAsync();
        connectionString = new NpgsqlConnectionStringBuilder(postgres.ConnectionString) { SearchPath = schema }.ConnectionString;
        Directory.CreateDirectory(directory);
        listener.Start();
        var origin = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}";
        var configuration = new CollectionConfiguration
        {
            People = [new PersonConfiguration { Id = "person", Name = "Fixture Official" }],
            Sources = [new WatchedSourceConfiguration
            {
                Id = "source", PersonIds = ["person"], Url = origin + "/page", AllowedOrigin = origin,
                AllowedPathPrefix = "/page", MinDelayMilliseconds = 0, TimeoutSeconds = 5
            }]
        };
        await File.WriteAllTextAsync(Path.Combine(directory, "config.json"),
            JsonSerializer.Serialize(configuration, CollectionProtocol.JsonOptions));
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
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"DROP SCHEMA {schema} CASCADE", connection);
        await command.ExecuteNonQueryAsync();
    }

    [Theory]
    [InlineData(200, "captured", 0)]
    [InlineData(404, "failed", 1)]
    [InlineData(429, "deferred", 1)]
    public async Task ExplicitMigrationsAndCollectionPersistOutcomes(int status, string outcome, int exitCode)
    {
        statusCode = status;
        Assert.Equal(0, (await HostProcess.RunAsync(connectionString, "db", "migrate")).ExitCode);
        Assert.Equal(0, (await HostProcess.RunAsync(connectionString, "db", "migrate")).ExitCode);
        var first = await CollectAsync("collect-import", connectionString);
        Assert.Equal(exitCode, first.ExitCode);
        using var output = JsonDocument.Parse(first.Output);
        var id = output.RootElement.GetProperty("attemptId").GetString()!;
        Assert.Contains(id, first.Error);
        Assert.Equal(outcome, output.RootElement.GetProperty("outcome").GetString());
        Assert.Equal("newAttempt", output.RootElement.GetProperty("importDisposition").GetString());
        Assert.Equal("notApplicable", output.RootElement.GetProperty("priorCaptureLinkStatus").GetString());

        var second = await CollectAsync("collect-import", connectionString);
        Assert.Equal(exitCode, second.ExitCode);
        using var repeated = JsonDocument.Parse(second.Output);
        Assert.NotEqual(id, repeated.RootElement.GetProperty("attemptId").GetString());
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var stored = new NpgsqlCommand("SELECT outcome FROM collection_attempts WHERE attempt_id = @id", connection);
        stored.Parameters.AddWithValue("id", id);
        Assert.Equal(outcome, await stored.ExecuteScalarAsync());
        await using var count = new NpgsqlCommand("SELECT count(*) FROM collection_attempts WHERE outcome = @outcome", connection);
        count.Parameters.AddWithValue("outcome", outcome);
        Assert.Equal(2L, await count.ExecuteScalarAsync());
        await using var captures = new NpgsqlCommand("SELECT count(*) FROM collection_captures", connection);
        Assert.Equal(status == 200 ? 1L : 0L, await captures.ExecuteScalarAsync());
        Assert.Equal(status == 200 ? 1 : 0, Directory.GetFiles(Path.Combine(directory, "captures"), "*.gz").Length);
    }

    [Fact]
    public async Task ManagedJobUsesSnapshotAndRepeatedRunDoesNotCollectAgain()
    {
        Assert.Equal(0, (await HostProcess.RunAsync(connectionString, "db", "migrate")).ExitCode);
        var arguments = new[] { "jobs", "enqueue", Path.Combine(directory, "config.json"), "source", "check-1" };
        var enqueued = await HostProcess.RunAsync(connectionString, arguments);
        Assert.Equal(0, enqueued.ExitCode);
        using var definition = JsonDocument.Parse(enqueued.Output);
        var id = definition.RootElement.GetProperty("jobId").GetString()!;
        var duplicate = await HostProcess.RunAsync(connectionString, arguments);
        using var duplicateOutput = JsonDocument.Parse(duplicate.Output);
        Assert.Equal(id, duplicateOutput.RootElement.GetProperty("jobId").GetString());
        File.Delete(Path.Combine(directory, "config.json"));
        var run = await HostProcess.RunAsync(connectionString, "jobs", "run", id,
            typeof(HttpCollector).Assembly.Location, Path.Combine(directory, "captures"));
        Assert.True(run.ExitCode == 0, run.Error + run.Output);
        using var result = JsonDocument.Parse(run.Output);
        Assert.Equal("succeeded", result.RootElement.GetProperty("job").GetProperty("state").GetString());
        shutdown.Cancel();
        listener.Stop();
        var again = await HostProcess.RunWithCollectorHostAsync(connectionString, "/no-collector-host",
            "jobs", "run", id, "/missing-collector.dll", Path.Combine(directory, "captures"));
        Assert.Equal(0, again.ExitCode);
        var inspected = await HostProcess.RunAsync(connectionString, "jobs", "get", id);
        using var inspection = JsonDocument.Parse(inspected.Output);
        Assert.Single(inspection.RootElement.GetProperty("attempts").EnumerateArray());
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var count = new NpgsqlCommand("SELECT count(*) FROM collection_attempts", connection);
        Assert.Equal(1L, await count.ExecuteScalarAsync());
    }

    [Fact]
    public async Task MissingMigrationsFailImportWithoutImplicitSchemaChangesOrSuccessOutput()
    {
        var result = await CollectAsync("collect-import", connectionString);
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("Attempt ID:", result.Error);
        Assert.Contains("Persistence was not confirmed", result.Error);
        Assert.Single(Directory.GetFiles(Path.Combine(directory, "captures"), "*.gz"));
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT to_regclass('collection_attempts') IS NULL", connection);
        Assert.Equal(true, await command.ExecuteScalarAsync());
    }

    [Fact]
    public async Task FailedImportCanReplayAfterRelocationWithoutConfigurationOrCollector()
    {
        var failed = await CollectAsync("collect-import", connectionString);
        Assert.Equal(1, failed.ExitCode);
        var original = Path.Combine(directory, "captures");
        var listed = await HostProcess.RunAsync(null, "receipts", "list", original);
        Assert.Equal(0, listed.ExitCode);
        var ids = JsonSerializer.Deserialize<string[]>(listed.Output)!;
        var id = Assert.Single(ids);
        Assert.Contains(id, failed.Error);

        var relocated = Path.Combine(directory, "relocated-captures");
        Directory.Move(original, relocated);
        File.Delete(Path.Combine(directory, "config.json"));
        shutdown.Cancel();
        listener.Stop();
        Assert.Equal(0, (await HostProcess.RunAsync(connectionString, "db", "migrate")).ExitCode);

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
        var failed = await CollectAsync("collect-import", connectionString);
        Assert.Equal(1, failed.ExitCode);
        var root = Path.Combine(directory, "captures");
        var invalid = Path.Combine(root, ".pending", "000-invalid.json");
        await File.WriteAllTextAsync(invalid, "{broken");
        Assert.Equal(0, (await HostProcess.RunAsync(connectionString, "db", "migrate")).ExitCode);

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
    public async Task TracerAndValidationRemainDatabaseFreeAndMissingDatabasePreventsCollection()
    {
        var validation = await HostProcess.RunAsync(null, "validate", Path.Combine(directory, "config.json"));
        Assert.Equal(0, validation.ExitCode);
        var missing = await CollectAsync("collect-import", null);
        Assert.Equal(2, missing.ExitCode);
        Assert.DoesNotContain("Attempt ID:", missing.Error);
        Assert.False(Directory.Exists(Path.Combine(directory, "captures")));
        var traced = await CollectAsync("collect", null);
        Assert.Equal(0, traced.ExitCode);
        using var receipt = JsonDocument.Parse(traced.Output);
        Assert.Equal("captured", receipt.RootElement.GetProperty("outcome").GetString());
        Assert.False(receipt.RootElement.TryGetProperty("attemptId", out _));
    }

    private Task<(int ExitCode, string Output, string Error)> CollectAsync(string command, string? database) =>
        HostProcess.RunAsync(database, command, Path.Combine(directory, "config.json"), "source",
            typeof(HttpCollector).Assembly.Location, Path.Combine(directory, "captures"));

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
            var body = !robots && status == 200 ? "Verified CLI evidence." : "";
            var response = $"HTTP/1.1 {status} Fixture\r\nContent-Length: {body.Length}\r\nConnection: close\r\nRetry-After: 10\r\n\r\n{body}";
            await stream.WriteAsync(Encoding.ASCII.GetBytes(response), shutdown.Token);
        }
    }
}
