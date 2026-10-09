using System.Text.Json;
using System.Globalization;
using System.Runtime.InteropServices;
using CivicLens.Application;
using CivicLens.Application.Collection;
using CivicLens.Application.Collection.Jobs;
using CivicLens.Application.Collection.Discovery;
using CivicLens.Application.Collection.Processing;
using CivicLens.Collection.Contracts;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Collection.Jobs;
using CivicLens.Infrastructure.Collection.Processing;
using CivicLens.Application.Documents;
using CivicLens.Host.Collection;
using CivicLens.Host.Documents;
using CivicLens.Host.Publication;
using CivicLens.Host.Review;
using CivicLens.Infrastructure.Documents;
using CivicLens.Application.Publication;
using CivicLens.Core.Review;
using CivicLens.Infrastructure.Publication;
using CivicLens.Infrastructure.Review;

if (args is [] or ["--help"] or ["help"])
{
    Console.WriteLine("""
        Civic Lens:
          status
          validate <config.json>
          collect <config.json> <source-id> <collector.dll> <artifact-directory> [--as-of yyyy-MM-dd]
          db migrate
          collect-import <config.json> <source-id> <collector.dll> <artifact-directory> [--as-of yyyy-MM-dd]
          receipts list <artifact-directory>
          receipts replay <artifact-directory> <attempt-id|--all>
          jobs enqueue <config.json> <source-id> <idempotency-key> [--as-of yyyy-MM-dd]
          jobs run <job-id> <collector.dll> <artifact-directory>
          jobs get <job-id>
          jobs list [limit]
          jobs cancel <job-id>
          worker <collector.dll> <artifact-directory> [--once]
          discovery get <attempt-id>
          discovery admit <config.json> <source-id> <attempt-id> <idempotency-key> [--as-of yyyy-MM-dd]
          documents extract <captured-attempt-id> <artifact-directory>
          documents extract <config.json> <captured-attempt-id> <artifact-directory>
          documents get <extraction-id>
          documents cite <extraction-id> <start> <length>
          documents history <source-id> <exact-requested-url>
          documents compare <before-extraction-id> <after-extraction-id>
          documents comparison <comparison-id>
          releases publish <idempotency-key> <draft-id>...
          releases list [limit]
          releases activate <release-number>
          review [port]
        The review workspace binds only to 127.0.0.1 and defaults to port 5081.
        New admissions default to today in UTC; existing keys retain their saved date when --as-of is omitted.
        --as-of selects eligibility, not historical fetching or attribution.
        feeds get/admit remain aliases for discovery get/admit.
        Database commands require CIVIC_LENS_DATABASE (Postgres connection string with Host and Database).
        collect and receipts list are database-free. collect-import saves a handoff before importing.
        receipts replay imports saved handoffs without collecting again; use the capture directory after relocation.
        jobs commands require the database. Each run reconciles prior work and performs at most one new fetch.
        documents commands require the database. Citations use UTF-16 offsets into immutable extracted text.
        Exit codes: 0 success, 1 failed/deferred collection or operational failure, 2 invalid input/configuration.
        """);
    return 0;
}

if (args is ["status"])
{
    Console.WriteLine(FoundationStatus.Description);
    return 0;
}

if (args.Length > 0 && args[0] == "review")
{
    if (args.Length > 2 || (args.Length == 2 &&
        (!int.TryParse(args[1], NumberStyles.None, CultureInfo.InvariantCulture, out var requestedPort) ||
         requestedPort is < 1 or > 65535)))
    {
        Console.Error.WriteLine("Use review [port], where port is an integer from 1 through 65535.");
        return 2;
    }
    var port = args.Length == 1 ? 5081 : int.Parse(args[1], CultureInfo.InvariantCulture);
    using var reviewCancellation = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; reviewCancellation.Cancel(); };
    return await ReviewWorkspace.RunAsync(port, reviewCancellation.Token);
}

var coverageAsOf = DateOnly.FromDateTime(DateTime.UtcNow);
DateOnly? admissionAsOf = null;
if (args.Contains("--as-of", StringComparer.Ordinal))
{
    if (args is not [.., "--as-of", var dateText] ||
        !DateOnly.TryParseExact(dateText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out coverageAsOf) ||
        args[..^2] is not (["collect" or "collect-import", _, _, _, _] or
            ["jobs", "enqueue", _, _, _] or ["feeds" or "discovery", "admit", _, _, _, _]))
    {
        Console.Error.WriteLine("Use a trailing --as-of yyyy-MM-dd with collect, collect-import, jobs enqueue, or discovery admit.");
        return 2;
    }
    admissionAsOf = coverageAsOf;
    args = args[..^2];
}

if (args is not (["db", "migrate"] or ["validate", _] or ["collect", _, _, _, _] or ["collect-import", _, _, _, _]
    or ["feeds" or "discovery", "get", _] or ["feeds" or "discovery", "admit", _, _, _, _]
    or ["receipts", "list", _] or ["receipts", "replay", _, _]) && !CollectionJobCommand.Matches(args) &&
    !CollectionWorkerCommand.Matches(args) && !DocumentCommand.Matches(args) && !ReleaseCommand.Matches(args))
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
    if (CollectionWorkerCommand.Matches(args))
    {
        using var terminate = OperatingSystem.IsWindows() ? null : PosixSignalRegistration.Create(PosixSignal.SIGTERM,
            context => { context.Cancel = true; cancellation.Cancel(); });
        CollectionWorkerCommand.ValidateArguments(args);
        var configurationPath = Environment.GetEnvironmentVariable("CIVIC_LENS_COLLECTION_CONFIG");
        if (string.IsNullOrWhiteSpace(configurationPath) || !Path.IsPathFullyQualified(configurationPath))
            throw new ArgumentException("Worker requires CIVIC_LENS_COLLECTION_CONFIG to name an absolute configuration path.");
        var workerConfiguration = await ReadConfigurationAsync(configurationPath, cancellation.Token);
        var jobs = CreateJobs();
        var attempts = CreateDatabase();
        var queue = PostgresCollectionWorkerQueue.FromConnectionString(Environment.GetEnvironmentVariable("CIVIC_LENS_DATABASE")!);
        executing = true;
        await jobs.SynchronizeAsync(workerConfiguration, cancellation.Token);
        var connectionString = Environment.GetEnvironmentVariable("CIVIC_LENS_DATABASE")!;
        var extractions = PostgresDocumentExtractionStore.FromConnectionString(connectionString);
        var comparisons = PostgresDocumentComparisonStore.FromConnectionString(connectionString);
        var processing = new EvidenceProcessingWorker(jobs, attempts, jobs,
            PostgresEvidenceProcessingStore.FromConnectionString(connectionString),
            new ExtractDocument(attempts, new CaptureDocumentTextExtractor(), extractions),
            new GetDocumentHistory(PostgresDocumentHistoryStore.FromConnectionString(connectionString)),
            new CompareDocuments(extractions, comparisons));
        await using var wakeup = PostgresCollectionPipelineWakeup.FromConnectionString(connectionString);
        return await CollectionWorkerCommand.ExecuteAsync(args, queue, jobs, attempts, cancellation.Token, processing, wakeup, jobs);
    }

    if (ReleaseCommand.Matches(args))
    {
        var connectionString = Environment.GetEnvironmentVariable("CIVIC_LENS_DATABASE");
        var releaseRoot = Environment.GetEnvironmentVariable("CIVIC_LENS_RELEASE_DIRECTORY");
        var ownerSubject = Environment.GetEnvironmentVariable("CIVIC_LENS_REVIEW_OWNER");
        if (string.IsNullOrWhiteSpace(connectionString) || string.IsNullOrWhiteSpace(releaseRoot) ||
            string.IsNullOrWhiteSpace(ownerSubject))
            throw new ArgumentException("Release commands require CIVIC_LENS_DATABASE, CIVIC_LENS_RELEASE_DIRECTORY, and CIVIC_LENS_REVIEW_OWNER.");
        var officialNames = new Dictionary<string, string>(StringComparer.Ordinal);
        if (args is ["releases", "publish", ..])
        {
            var configurationPath = Environment.GetEnvironmentVariable("CIVIC_LENS_COLLECTION_CONFIG");
            if (string.IsNullOrWhiteSpace(configurationPath) || !Path.IsPathFullyQualified(configurationPath))
                throw new ArgumentException("Publishing requires CIVIC_LENS_COLLECTION_CONFIG to name an absolute configuration path.");
            var releaseConfiguration = await ReadConfigurationAsync(configurationPath, cancellation.Token);
            foreach (var person in releaseConfiguration.People) officialNames.Add(person.Id, person.Name);
        }
        var publications = PostgresPublicationStore.FromConnectionString(connectionString);
        var releases = new FileReleaseDirectory(releaseRoot);
        var publish = new PublishDocumentChanges(publications, releases, new ReleaseRenderer(),
            PostgresDocumentChangeReviewStore.FromConnectionString(connectionString),
            new PublicationCatalog(officialNames), TimeProvider.System);
        executing = true;
        return await ReleaseCommand.ExecuteAsync(args, publish, new ListPublicationReleases(publications, releases),
            new ActivatePublicationRelease(publications, releases), new ReviewActor(ownerSubject, ReviewRole.Owner),
            cancellation.Token);
    }

    if (DocumentCommand.Matches(args))
    {
        var documentConfiguration = args is ["documents", "extract", var documentConfigPath, _, _]
            ? await ReadConfigurationAsync(documentConfigPath, cancellation.Token) : null;
        var attempts = CreateDatabase();
        var extractions = PostgresDocumentExtractionStore.FromConnectionString(Environment.GetEnvironmentVariable("CIVIC_LENS_DATABASE")!);
        executing = true;
        return await DocumentCommand.ExecuteAsync(args, attempts, new CaptureDocumentTextExtractor(), extractions,
            cancellation.Token, documentConfiguration,
            PostgresDocumentHistoryStore.FromConnectionString(Environment.GetEnvironmentVariable("CIVIC_LENS_DATABASE")!),
            PostgresDocumentComparisonStore.FromConnectionString(Environment.GetEnvironmentVariable("CIVIC_LENS_DATABASE")!));
    }

    if (args is ["feeds" or "discovery", "get", var discoveryId])
    {
        if (!PendingCollectionHandoff.IsValidAttemptId(discoveryId)) throw new ArgumentException("Invalid attempt ID.");
        var evidence = CreateDatabase();
        executing = true;
        return await DiscoveryCommand.InspectAsync(evidence, discoveryId, cancellation.Token);
    }

    if (args is ["feeds" or "discovery", "admit", var discoveryConfigPath, var discoverySourceId, var discoveryAttemptId, var admissionKey])
    {
        var config = await ReadConfigurationAsync(discoveryConfigPath, cancellation.Token);
        var source = new ConfiguredCollectionSource(config, discoverySourceId, admissionAsOf);
        var jobs = CreateJobs();
        executing = true;
        return await DiscoveryCommand.AdmitAsync(jobs, source, discoveryAttemptId, admissionKey, cancellation.Token);
    }

    if (args is ["jobs", "enqueue", var configPath, var sourceId, var idempotencyKey])
    {
        var config = await ReadConfigurationAsync(configPath, cancellation.Token);
        var source = new ConfiguredCollectionSource(config, sourceId, admissionAsOf);
        var jobs = CreateJobs();
        executing = true;
        return await CollectionJobCommand.EnqueueAsync(jobs, source, idempotencyKey, cancellation.Token);
    }

    if (args[0] == "jobs")
    {
        CollectionJobCommand.ValidateArguments(args);
        var jobs = CreateJobs();
        var evidence = CreateDatabase();
        executing = true;
        return await CollectionJobCommand.ExecuteAsync(args, jobs, evidence, cancellation.Token);
    }

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
        var attempt = new PreparedCollectionAttempt(configuration, args[2], args[4], coverageAsOf);
        Console.Error.WriteLine($"Attempt ID: {attempt.AttemptId}");
        executing = true;
        var completion = await new CollectAndImportCollectionAttempt(runner, database,
            new FileCollectionReceiptHandoffStore(attempt.Request.ArtifactDirectory))
            .ExecuteAsync(attempt.AttemptId, attempt.Request, cancellation.Token);
        return CollectionCommandOutput.WriteCompletion(completion);
    }

    // Validate the selected source before entering operational execution.
    configuration.CreateRequest(args[2], "validation-job", Path.GetFullPath(args[4]), coverageAsOf);
    executing = true;
    var result = await new CollectWatchedPage(runner).ExecuteAsync(configuration, args[2], args[4], cancellation.Token, coverageAsOf);
    Console.WriteLine(JsonSerializer.Serialize(result, CollectionProtocol.JsonOptions));
    return result.Outcome is CollectionOutcome.Captured or CollectionOutcome.NotModified ? 0 : 1;
}
catch (OperationCanceledException)
{
    if (args[0] == "jobs" && executing)
        Console.Error.WriteLine("Job execution interrupted. Use jobs get to inspect retained progress; jobs cancel requests permanent cancellation.");
    else if (replaying && executing)
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
    if (args[0] == "jobs")
        Console.Error.WriteLine("Job operation failed. Inspect the job, retained receipts, database connectivity, and applied migrations before resuming.");
    else if (replaying)
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

static PostgresCollectionJobStore CreateJobs()
{
    var connectionString = Environment.GetEnvironmentVariable("CIVIC_LENS_DATABASE");
    if (string.IsNullOrWhiteSpace(connectionString)) throw new ArgumentException("CIVIC_LENS_DATABASE is required.");
    return PostgresCollectionJobStore.FromConnectionString(connectionString);
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
