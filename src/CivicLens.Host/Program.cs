using System.Text.Json;
using CivicLens.Application;
using CivicLens.Application.Collection;
using CivicLens.Collection.Contracts;
using CivicLens.Core.Collection;
using CivicLens.Infrastructure.Collection;
using CivicLens.Host.Collection;

if (args is [] or ["--help"] or ["help"])
{
    Console.WriteLine("""
        Civic Lens:
          status
          validate <config.json>
          collect <config.json> <source-id> <collector.dll> <artifact-directory>
          db migrate
          collect-import <config.json> <source-id> <collector.dll> <artifact-directory>
        Database commands require CIVIC_LENS_DATABASE (Postgres connection string with Host and Database).
        collect is database-free. collect-import creates a fresh attempt; it does not replay saved receipts.
        Exit codes: 0 success, 1 failed/deferred collection or operational failure, 2 invalid input/configuration.
        """);
    return 0;
}

if (args is ["status"])
{
    Console.WriteLine(FoundationStatus.Description);
    return 0;
}

if (args is not (["db", "migrate"] or ["validate", _] or ["collect", _, _, _, _] or ["collect-import", _, _, _, _]))
{
    Console.Error.WriteLine("Unknown command. Use --help.");
    return 2;
}

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
var executing = false;
var importing = args[0] == "collect-import";
try
{
    if (args is ["db", "migrate"])
    {
        var database = CreateDatabase();
        executing = true;
        await database.MigrateAsync(cancellation.Token);
        Console.WriteLine("Database migrations applied.");
        return 0;
    }

    var configuration = await ReadConfigurationAsync(args[1], cancellation.Token);
    if (args[0] == "validate")
    {
        Console.WriteLine($"Valid configuration: {configuration.People.Length} people, {configuration.Sources.Length} watched sources.");
        return 0;
    }

    var runner = new CollectorProcess(args[3], Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet");
    if (importing)
    {
        var database = CreateDatabase();
        var attempt = new PreparedCollectionAttempt(configuration, args[2], args[4]);
        Console.Error.WriteLine($"Attempt ID: {attempt.AttemptId}");
        executing = true;
        var decision = await new CollectAndImportCollectionAttempt(runner, database)
            .ExecuteAsync(attempt.AttemptId, attempt.Request, cancellation.Token);
        var outcome = decision.AttemptResult switch
        {
            CapturedAttemptResult => CollectionOutcome.Captured,
            NotModifiedAttemptResult => CollectionOutcome.NotModified,
            FailedAttemptResult => CollectionOutcome.Failed,
            DeferredAttemptResult => CollectionOutcome.Deferred,
            _ => throw new InvalidOperationException("Unknown collection outcome.")
        };
        var output = new CollectionImportOutput(attempt.AttemptId, outcome,
            JsonNamingPolicy.CamelCase.ConvertName(decision.Disposition.ToString()),
            JsonNamingPolicy.CamelCase.ConvertName(decision.PriorCaptureLinkStatus.ToString()));
        Console.WriteLine(JsonSerializer.Serialize(output, CollectionProtocol.JsonOptions));
        return outcome is CollectionOutcome.Captured or CollectionOutcome.NotModified ? 0 : 1;
    }

    // Validate the selected source before entering operational execution.
    configuration.CreateRequest(args[2], "validation-job", Path.GetFullPath(args[4]));
    executing = true;
    var result = await new CollectWatchedPage(runner).ExecuteAsync(configuration, args[2], args[4], cancellation.Token);
    Console.WriteLine(JsonSerializer.Serialize(result, CollectionProtocol.JsonOptions));
    return result.Outcome is CollectionOutcome.Captured or CollectionOutcome.NotModified ? 0 : 1;
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine(importing && executing
        ? "Collection/import cancelled or timed out. Persistence was not confirmed."
        : "Command cancelled or timed out.");
    return 1;
}
catch (Exception exception) when (!executing && exception is ArgumentException or UnauthorizedAccessException or IOException or JsonException or InvalidOperationException)
{
    // Configuration and provider exceptions may contain credentials or other input values.
    Console.Error.WriteLine("Invalid input/configuration. Check command paths, source configuration, and CIVIC_LENS_DATABASE for database commands.");
    return 2;
}
catch (Exception)
{
    Console.Error.WriteLine(importing
        ? "Collection/import failed. Persistence was not confirmed. Check the collector, capture directory, database connectivity, and applied migrations."
        : "Command failed. Check filesystem access, collector availability, or database connectivity as applicable.");
    return 1;
}

static PostgresCollectionAttemptStore CreateDatabase()
{
    var connectionString = Environment.GetEnvironmentVariable("CIVIC_LENS_DATABASE");
    if (string.IsNullOrWhiteSpace(connectionString))
        throw new ArgumentException("CIVIC_LENS_DATABASE is required.");
    return PostgresCollectionAttemptStore.FromConnectionString(connectionString);
}

static async Task<CollectionConfiguration> ReadConfigurationAsync(string path, CancellationToken cancellationToken)
{
    await using var input = File.OpenRead(path);
    var buffer = new byte[1_000_001];
    var count = await input.ReadAtLeastAsync(buffer, buffer.Length, throwOnEndOfStream: false, cancellationToken);
    if (count > 1_000_000)
        throw new ArgumentException("Configuration exceeds 1 MB.");
    var configuration = JsonSerializer.Deserialize<CollectionConfiguration>(
        buffer.AsSpan(0, count), CollectionProtocol.JsonOptions)
        ?? throw new ArgumentException("Configuration must not be null.");
    configuration.Validate();
    return configuration;
}
