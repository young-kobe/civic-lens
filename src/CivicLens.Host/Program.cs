using System.Text.Json;
using CivicLens.Application;
using CivicLens.Application.Collection;
using CivicLens.Collection.Contracts;
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
          receipts list <artifact-directory>
          receipts replay <artifact-directory> <attempt-id|--all>
        Database commands require CIVIC_LENS_DATABASE (Postgres connection string with Host and Database).
        collect and receipts list are database-free. collect-import saves a handoff before importing.
        receipts replay imports saved handoffs without collecting again; use the capture directory after relocation.
        Exit codes: 0 success, 1 failed/deferred collection or operational failure, 2 invalid input/configuration.
        """);
    return 0;
}

if (args is ["status"])
{
    Console.WriteLine(FoundationStatus.Description);
    return 0;
}

if (args is not (["db", "migrate"] or ["validate", _] or ["collect", _, _, _, _] or ["collect-import", _, _, _, _]
    or ["receipts", "list", _] or ["receipts", "replay", _, _]))
{
    Console.Error.WriteLine("Unknown command. Use --help.");
    return 2;
}

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
var executing = false;
var replaying = args is ["receipts", "replay", _, _];
var importing = args[0] == "collect-import" || replaying;
try
{
    if (args is ["receipts", "list", var listRoot])
    {
        var handoffs = new FileCollectionReceiptHandoffStore(Path.GetFullPath(listRoot));
        executing = true;
        var pending = await handoffs.ListAsync(cancellation.Token);
        Console.WriteLine(JsonSerializer.Serialize(pending, CollectionProtocol.JsonOptions));
        return 0;
    }

    if (args is ["receipts", "replay", var replayRoot, var selection])
    {
        if (selection != "--all" && !PendingCollectionHandoff.IsValidAttemptId(selection))
            throw new ArgumentException("Invalid attempt ID.");
        var root = Path.GetFullPath(replayRoot);
        var database = CreateDatabase();
        var recovery = new RecoverCollectionAttempts(new FileCollectionReceiptHandoffStore(root),
            new CaptureArtifactVerifier(), database);
        executing = true;
        return await CollectionRecoveryCommand.ExecuteAsync(recovery, root, selection, cancellation.Token);
    }

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
        var completion = await new CollectAndImportCollectionAttempt(runner, database,
            new FileCollectionReceiptHandoffStore(attempt.Request.ArtifactDirectory))
            .ExecuteAsync(attempt.AttemptId, attempt.Request, cancellation.Token);
        return CollectionCommandOutput.WriteCompletion(completion);
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
    if (replaying && executing)
        Console.Error.WriteLine("Receipt replay cancelled or timed out. Persistence was not confirmed. Use receipts list to inspect retained handoffs.");
    else
        Console.Error.WriteLine(importing && executing
            ? "Collection/import cancelled or timed out. Persistence was not confirmed. Use receipts list and receipts replay to recover saved handoffs."
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
    if (replaying)
        Console.Error.WriteLine("Receipt replay failed. Persistence was not confirmed. Check the capture directory, retained handoff, database connectivity, and applied migrations.");
    else
        Console.Error.WriteLine(importing
            ? "Collection/import failed. Persistence was not confirmed. Check the collector, capture directory, database connectivity, and applied migrations. Use receipts list and receipts replay to recover saved handoffs."
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
