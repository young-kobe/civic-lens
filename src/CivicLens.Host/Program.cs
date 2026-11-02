using System.Text.Json;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Runtime.InteropServices;
using CivicLens.Application;
using CivicLens.Application.Analysis;
using CivicLens.Application.Issues;
using CivicLens.Core.Analysis;
using CivicLens.Infrastructure.Analysis;
using CivicLens.Application.Collection;
using CivicLens.Application.Collection.Bills;
using CivicLens.Application.Collection.Jobs;
using CivicLens.Application.Collection.Discovery;
using CivicLens.Application.Collection.Processing;
using CivicLens.Collection.Contracts;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Collection.Jobs;
using CivicLens.Infrastructure.Collection.Processing;
using CivicLens.Application.Documents;
using CivicLens.Host.Analysis;
using CivicLens.Application.Actions;
using CivicLens.Application.Votes;
using CivicLens.Infrastructure.Votes;
using CivicLens.Host;
using CivicLens.Host.Actions;
using CivicLens.Host.Briefs;
using CivicLens.Host.Relationships;
using CivicLens.Host.Roster;
using CivicLens.Application.Roster;
using CivicLens.Infrastructure.Roster;
using CivicLens.Host.Votes;
using CivicLens.Application.Briefs;
using CivicLens.Application.Relationships;
using CivicLens.Infrastructure.Relationships;
using CivicLens.Infrastructure.Briefs;
using CivicLens.Infrastructure.Actions;
using CivicLens.Host.Collection;
using CivicLens.Host.Documents;
using CivicLens.Host.Publication;
using CivicLens.Host.Reports;
using CivicLens.Host.Review;
using CivicLens.Host.Statements;
using CivicLens.Application.Statements;
using CivicLens.Infrastructure.Statements;
using CivicLens.Infrastructure.Documents;
using CivicLens.Application.Publication;
using CivicLens.Core.Review;
using CivicLens.Infrastructure.Publication;
using CivicLens.Infrastructure.Review;

if (args is [] or ["--help"] or ["help"])
{
    Console.WriteLine("""
        Civic Lens:
          validate <config.json>
          collect <config.json> <source-id> <collector.dll> <artifact-directory> [--as-of yyyy-MM-dd]
          db migrate
          receipts list <artifact-directory>
          receipts replay <artifact-directory> <attempt-id|--all>
          jobs enqueue <config.json> <source-id> <idempotency-key> [--as-of yyyy-MM-dd]
          jobs get <job-id>
          jobs list [limit]
          jobs cancel <job-id>
          worker <collector.dll> <artifact-directory> [--once]
          worker health
          discovery get <attempt-id>
          discovery admit <config.json> <source-id> <attempt-id> <idempotency-key> [--as-of yyyy-MM-dd]
          documents extract <captured-attempt-id> <artifact-directory>
          documents extract <config.json> <captured-attempt-id> <artifact-directory>
          documents get <extraction-id>
          documents cite <extraction-id> <start> <length>
          documents history <source-id> <exact-requested-url>
          documents compare <before-extraction-id> <after-extraction-id>
          documents comparison <comparison-id>
          analyses requeue <comparison-id|--all>
          statements status
          statements list [passed|skipped|failed-open|over-cap|pending|failed] [limit]
          statements run <run-id>
          statements requeue <candidate-id|--all>
          statements regate [--since yyyy-MM-dd]
          statements draft status
          statements draft list [drafted|over-cap|existing|pending|failed] [limit]
          statements draft run <run-id>
          statements draft requeue <candidate-id|--all>
          actions draft status
          actions draft list [drafted|unchanged|superseded|no-official|pending|failed] [limit]
          actions draft requeue <candidate-id|--all>
          actions draft redraft
          votes draft status
          votes draft list [drafted|unchanged|superseded|no-member|pending|failed] [limit]
          votes draft requeue <candidate-id|--all>
          votes draft requeue --blocked --code <block-code>
          votes draft redraft
          bills subjects backfill [limit]
          bills status backfill [limit]
          relationships draft status
          relationships draft list [drafted|unchanged|none|unmatched|pending|failed] [limit]
          relationships draft requeue <pair-id|--all>
          relationships draft refresh
          brief redraft --week <yyyy-MM-dd>
          roster propose <config.json>
          releases publish <idempotency-key> <draft-id>...
          releases list [limit]
          releases activate <release-number>
          review [port]
        The review workspace binds only to 127.0.0.1 and defaults to port 5081.
        New admissions default to today in UTC; existing keys retain their saved date when --as-of is omitted.
        --as-of selects eligibility, not historical fetching or attribution.
        Database commands require CIVIC_LENS_DATABASE (Postgres connection string with Host and Database).
        collect and receipts list are database-free.
        receipts replay imports saved handoffs without collecting again; use the capture directory after relocation.
        jobs commands require the database.
        worker also drafts document-change accounts when CIVIC_LENS_ANALYSIS_DAILY_TOKENS and ANTHROPIC_API_KEY are set.
        worker also gates statement candidates when CIVIC_LENS_STATEMENT_GATE_DAILY_TOKENS, ANTHROPIC_API_KEY, and CIVIC_LENS_ISSUES_CONFIG are set.
        worker requires CIVIC_LENS_ISSUES_CONFIG when a Congress.gov bill list source is enabled.
        worker also drafts statements when CIVIC_LENS_STATEMENT_DRAFTING_DAILY_TOKENS and ANTHROPIC_API_KEY are set.
        worker also drafts bill actions when CIVIC_LENS_ACTION_DRAFTING_DAILY_TOKENS, ANTHROPIC_API_KEY, and CIVIC_LENS_ISSUES_CONFIG are set.
        worker also drafts House and Senate votes when CIVIC_LENS_VOTE_DRAFTING_DAILY_TOKENS, ANTHROPIC_API_KEY, and CIVIC_LENS_ISSUES_CONFIG are set.
        worker also drafts relationships when CIVIC_LENS_RELATIONSHIP_DRAFTING_DAILY_TOKENS and ANTHROPIC_API_KEY are set.
        worker also drafts weekly briefs when CIVIC_LENS_BRIEF_DRAFTING_DAILY_TOKENS and ANTHROPIC_API_KEY are set.
        documents commands require the database. Citations use UTF-16 offsets into immutable extracted text.
        Exit codes: 0 success, 1 failed/deferred collection or operational failure, 2 invalid input/configuration.
        """);
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
        args[..^2] is not (["collect", _, _, _, _] or
            ["jobs", "enqueue", _, _, _] or ["discovery", "admit", _, _, _, _]))
    {
        Console.Error.WriteLine("Use a trailing --as-of yyyy-MM-dd with collect, jobs enqueue, or discovery admit.");
        return 2;
    }
    admissionAsOf = coverageAsOf;
    args = args[..^2];
}

if (args is not (["db", "migrate"] or ["validate", _] or ["collect", _, _, _, _]
    or ["discovery", "get", _] or ["discovery", "admit", _, _, _, _]
    or ["receipts", "list", _] or ["receipts", "replay", _, _]) && !CollectionJobCommand.Matches(args) &&
    !CollectionWorkerCommand.Matches(args) && !WorkerHealthCommand.Matches(args) && !DocumentCommand.Matches(args) && !ReleaseCommand.Matches(args) &&
    !AnalysisCommand.Matches(args) && !StatementCommand.Matches(args) && !ActionCommand.Matches(args) && !VoteCommand.Matches(args) && !RelationshipCommand.Matches(args) && !BillBackfillCommand.Matches(args) && !BriefCommand.Matches(args) && !RosterCommand.Matches(args))
{
    Console.Error.WriteLine("Unknown command. Use --help.");
    return 2;
}

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
var executing = false;
var replaying = args is ["receipts", "replay", _, _];
try
{
    if (CollectionWorkerCommand.Matches(args))
    {
        using var terminate = OperatingSystem.IsWindows() ? null : PosixSignalRegistration.Create(PosixSignal.SIGTERM,
            context => { context.Cancel = true; cancellation.Cancel(); });
        CollectionWorkerCommand.ValidateArguments(args);
        using var loggers = CreateLoggers();
        var workerConfiguration = ConfigurationFile.ReadCollectionFromEnvironment()
            ?? throw new ArgumentException("Worker requires CIVIC_LENS_COLLECTION_CONFIG.");
        var issueConfiguration = ConfigurationFile.ReadIssuesFromEnvironment();
        var issues = StatementIssueRubric.From(issueConfiguration);
        if (issues is null && workerConfiguration.Sources.Any(source => source.Enabled && source.ApiParser == ApiParser.CongressBillList))
            throw new ArgumentException("Worker requires CIVIC_LENS_ISSUES_CONFIG when a Congress.gov bill list source is enabled.");
        var evidenceConcurrency = EvidenceProcessingConfiguration.ReadConcurrency();
        var collectionConcurrency = CollectionLaneConfiguration.ReadConcurrency();
        var jobs = CreateJobs();
        var attempts = CreateDatabase();
        var connectionString = Environment.GetEnvironmentVariable("CIVIC_LENS_DATABASE")!;
        var queue = PostgresCollectionWorkerQueue.FromConnectionString(connectionString);
        var analysisContexts = AnalysisDbContextFactory.Create(connectionString);
        var heartbeats = PostgresWorkerHeartbeatStore.FromConnectionString(connectionString);
        WorkerBeat BeatOf(string loop) => new(heartbeats, loop, loggers.CreateLogger<WorkerBeat>(), TimeProvider.System);
        var analysis = DocumentChangeDraftComposition.Create(analysisContexts, connectionString,
            issueConfiguration?.Issues.Select(issue => issue.Id) ?? [], BeatOf(WorkerLoopNames.Drafting), loggers);
        await using var analysisWakeup = analysis.Wakeup;
        var gate = StatementGateComposition.Create(analysisContexts, connectionString, issues, BeatOf(WorkerLoopNames.Gate), loggers);
        await using var gateWakeup = gate.Wakeup;
        var statementDrafting = StatementDraftComposition.Create(analysisContexts, connectionString, issueConfiguration ?? IssueConfiguration.None, BeatOf(WorkerLoopNames.StatementDrafting), loggers);
        await using var statementDraftingWakeup = statementDrafting.Wakeup;
        var textExtractor = new CaptureDocumentTextExtractor();
        var apiResponses = new ApiResponseReader(attempts, new CaptureBodyReader());
        var artifactRoot = CollectionWorkerCommand.ArtifactRootOf(args);
        var actionDrafting = ActionDraftComposition.Create(analysisContexts, connectionString, jobs, apiResponses, textExtractor, issues,
            artifactRoot, BeatOf(WorkerLoopNames.ActionDrafting), loggers);
        await using var actionDraftingWakeup = actionDrafting.Wakeup;
        var voteDrafting = VoteDraftComposition.Create(analysisContexts, connectionString, jobs, apiResponses, issues, artifactRoot, BeatOf(WorkerLoopNames.VoteDrafting), loggers);
        await using var voteDraftingWakeup = voteDrafting.Wakeup;
        var relationshipDrafting = RelationshipDraftComposition.Create(analysisContexts, connectionString, workerConfiguration, BeatOf(WorkerLoopNames.RelationshipDrafting), loggers);
        await using var relationshipDraftingWakeup = relationshipDrafting.Wakeup;
        var briefDrafting = BriefDraftComposition.Create(analysisContexts, connectionString, workerConfiguration, BeatOf(WorkerLoopNames.BriefDrafting), loggers);
        await using var briefDraftingWakeup = briefDrafting.Wakeup;
        var publishing = PublishingComposition.Create(connectionString, workerConfiguration, issueConfiguration, artifactRoot, BeatOf(WorkerLoopNames.Publishing), loggers);
        await using var publishingWakeup = publishing?.Wakeup;
        var reportPull = ReportPullComposition.Create(analysisContexts, connectionString, BeatOf(WorkerLoopNames.ReportPull), loggers);
        await using var reportPullWakeup = reportPull?.Wakeup;
        executing = true;
        await jobs.SynchronizeAsync(workerConfiguration, cancellation.Token);
        if (!CollectionWorkerCommand.IsOnce(args)) await heartbeats.ClearAsync(cancellation.Token);
        await analysis.RecordStartAsync(cancellation.Token);
        await gate.RecordStartAsync(cancellation.Token);
        await statementDrafting.RecordStartAsync(cancellation.Token);
        await actionDrafting.RecordStartAsync(cancellation.Token);
        await voteDrafting.RecordStartAsync(cancellation.Token);
        await relationshipDrafting.RecordStartAsync(cancellation.Token);
        await briefDrafting.RecordStartAsync(cancellation.Token);
        var extractions = PostgresDocumentExtractionStore.FromConnectionString(connectionString);
        var comparisons = PostgresDocumentComparisonStore.FromConnectionString(connectionString);
        var processingStore = PostgresEvidenceProcessingStore.FromConnectionString(connectionString);
        var processing = new EvidenceProcessingWorker(jobs,
            new JobPreparation(jobs, attempts, jobs, jobs, jobs, jobs, PostgresMemberRosterStore.FromConnectionString(connectionString),
                PostgresExecutiveRosterStore.FromConnectionString(connectionString),
                new CaptureBodyReader(), processingStore, extractions, textExtractor, issues, loggers.CreateLogger<JobPreparation>()),
            processingStore,
            new ExtractDocument(attempts, textExtractor, extractions),
            new GetDocumentHistory(PostgresDocumentHistoryStore.FromConnectionString(connectionString)),
            new CompareDocuments(comparisons),
            evidenceConcurrency, loggers.CreateLogger<EvidenceProcessingWorker>());
        await using var wakeup = PostgresCollectionPipelineWakeup.FromConnectionString(connectionString);
        return await CollectionWorkerCommand.ExecuteAsync(args, queue, jobs, attempts, loggers, BeatOf(WorkerLoopNames.Collection), cancellation.Token, processing, wakeup, jobs,
            [.. new[]
            {
                AnalysisStage.For(WorkerLoopNames.Drafting, analysis.Worker),
                AnalysisStage.For(WorkerLoopNames.Gate, gate.Worker),
                AnalysisStage.For(WorkerLoopNames.StatementDrafting, statementDrafting.Worker),
                AnalysisStage.For(WorkerLoopNames.ActionDrafting, actionDrafting.Worker),
                AnalysisStage.For(WorkerLoopNames.VoteDrafting, voteDrafting.Worker),
                AnalysisStage.For(WorkerLoopNames.RelationshipDrafting, relationshipDrafting.Worker),
                AnalysisStage.For(WorkerLoopNames.BriefDrafting, briefDrafting.Worker),
                publishing?.Stage,
                reportPull?.Stage
            }.OfType<AnalysisStage>()], collectionConcurrency);
    }

    if (WorkerHealthCommand.Matches(args))
    {
        var connectionString = Environment.GetEnvironmentVariable("CIVIC_LENS_DATABASE");
        if (string.IsNullOrWhiteSpace(connectionString)) throw new ArgumentException("worker health requires CIVIC_LENS_DATABASE.");
        executing = true;
        return await WorkerHealthCommand.ExecuteAsync(PostgresWorkerHeartbeatStore.FromConnectionString(connectionString), Console.Out, cancellation.Token);
    }

    if (AnalysisCommand.Matches(args))
    {
        var (connectionString, owner) = OwnerCommand.ReadEnvironment();
        var requeue = new RequeueAnalyses<AnalysisRun>(PostgresDocumentChangeAnalysisStore.FromConnectionString(connectionString));
        executing = true;
        return await AnalysisCommand.ExecuteAsync(args, requeue, owner, cancellation.Token);
    }

    if (StatementCommand.Matches(args))
    {
        var (connectionString, owner) = OwnerCommand.ReadEnvironment();
        var contexts = AnalysisDbContextFactory.Create(connectionString);
        var gates = new PostgresStatementGateStore(contexts);
        var gateCommands = new AnalysisQueueCommands<StatementGateRun, StatementGateView, StatementGateListItem>(gates,
            new PostgresAnalysisStatusStore(contexts, StatementGateTask.Task));
        var draftCommands = new AnalysisQueueCommands<StatementDraftRun, StatementDraftView, StatementDraftListItem>(
            new PostgresStatementDraftStore(contexts),
            new PostgresAnalysisStatusStore(contexts, StatementDraftTask.Task));
        var statements = new StatementCommand(gateCommands, draftCommands, new RegateStatementCandidates(gates),
            new ExportGateLabels(new PostgresGateLabelStore(contexts)), owner,
            args is ["statements", "regate", ..] ? StatementIssueRubric.FromEnvironment()?.Id : null, GateLabelExport.DefaultRuntimeDirectory());
        executing = true;
        return await statements.ExecuteAsync(args, cancellation.Token);
    }

    if (ActionCommand.Matches(args))
    {
        var (connectionString, owner) = OwnerCommand.ReadEnvironment();
        var contexts = AnalysisDbContextFactory.Create(connectionString);
        var draftStore = new PostgresActionDraftStore(contexts);
        var draftCommands = new AnalysisQueueCommands<ActionDraftRun, ActionDraftView, ActionDraftListItem>(
            draftStore, new PostgresAnalysisStatusStore(contexts, ActionDraftTask.Task));
        var actions = new ActionCommand(draftCommands, new RequeueBlockedActionDrafts(draftStore), new RedraftActionDrafts(draftStore), owner);
        executing = true;
        return await actions.ExecuteAsync(args, cancellation.Token);
    }

    if (VoteCommand.Matches(args))
    {
        var (connectionString, owner) = OwnerCommand.ReadEnvironment();
        var contexts = AnalysisDbContextFactory.Create(connectionString);
        var draftStore = new PostgresVoteDraftStore(contexts);
        var draftCommands = new AnalysisQueueCommands<VoteDraftRun, VoteDraftView, VoteDraftListItem>(
            draftStore, new PostgresAnalysisStatusStore(contexts, VoteDraftTask.Task));
        var votes = new VoteCommand(draftCommands, new GetVoteDraftStatus(draftCommands.Status, draftStore),
            new RequeueBlockedVoteDrafts(draftStore), new RedraftVoteDrafts(draftStore), owner);
        executing = true;
        return await votes.ExecuteAsync(args, cancellation.Token);
    }

    if (RelationshipCommand.Matches(args))
    {
        var (connectionString, owner) = OwnerCommand.ReadEnvironment();
        var contexts = AnalysisDbContextFactory.Create(connectionString);
        var draftStore = new PostgresRelationshipDraftStore(contexts);
        var draftCommands = new AnalysisQueueCommands<RelationshipDraftRun, RelationshipDraftView, RelationshipDraftListItem>(
            draftStore, new PostgresAnalysisStatusStore(contexts, RelationshipDraftTask.Task));
        var relationships = new RelationshipCommand(draftCommands, new GetRelationshipDraftStatus(draftCommands.Status, draftStore), new RefreshRelationshipPairs(draftStore), owner);
        executing = true;
        return await relationships.ExecuteAsync(args, cancellation.Token);
    }

    if (BillBackfillCommand.Matches(args))
    {
        var (_, owner) = OwnerCommand.ReadEnvironment();
        var jobs = CreateJobs();
        var responses = new ApiResponseReader(CreateDatabase(), new CaptureBodyReader());
        var artifactRoot = PublicationComposition.RequireArtifactRoot();
        executing = true;
        return await new BillBackfillCommand(new BackfillBillSubjects(jobs, jobs, responses, artifactRoot),
            new BackfillBillStatus(jobs, jobs, responses, artifactRoot), owner).ExecuteAsync(args, cancellation.Token);
    }

    if (BriefCommand.Matches(args))
    {
        var (connectionString, owner) = OwnerCommand.ReadEnvironment();
        var brief = new BriefCommand(new RedraftBrief(new PostgresBriefDraftStore(AnalysisDbContextFactory.Create(connectionString))), owner);
        executing = true;
        return await brief.ExecuteAsync(args, cancellation.Token);
    }

    if (RosterCommand.Matches(args))
    {
        var rosterConfiguration = args is ["roster", "propose", var rosterConfigPath]
            ? ConfigurationFile.ReadCollection(rosterConfigPath) : null;
        var roster = PostgresMemberRosterStore.FromConnectionString(Environment.GetEnvironmentVariable("CIVIC_LENS_DATABASE")!);
        executing = true;
        return await RosterCommand.ExecuteAsync(args, rosterConfiguration, new ProposeRosterChanges(roster), cancellation.Token);
    }

    if (ReleaseCommand.Matches(args))
    {
        var (connectionString, owner) = OwnerCommand.ReadEnvironment();
        var releaseRoot = Environment.GetEnvironmentVariable("CIVIC_LENS_RELEASE_DIRECTORY");
        if (string.IsNullOrWhiteSpace(releaseRoot))
            throw new ArgumentException("Release commands require CIVIC_LENS_RELEASE_DIRECTORY.");
        var publishConfiguration = args is ["releases", "publish", ..]
            ? ConfigurationFile.ReadCollectionFromEnvironment()
                ?? throw new ArgumentException("Publishing requires CIVIC_LENS_COLLECTION_CONFIG.")
            : new CollectionConfiguration { People = [], Sources = [] };
        var issueConfiguration = args is ["releases", "publish", ..]
            ? PublicationComposition.RequireIssues(ConfigurationFile.ReadIssuesFromEnvironment())
            : IssueConfiguration.None;
        var artifactRoot = args is ["releases", "publish", ..] ? PublicationComposition.RequireArtifactRoot() : string.Empty;
        var publications = PostgresPublicationStore.FromConnectionString(connectionString);
        var releases = new FileReleaseDirectory(releaseRoot);
        using var loggers = CreateLoggers();
        var (publish, _) = PublicationComposition.Create(connectionString, publications, releases, publishConfiguration, issueConfiguration,
            artifactRoot, loggers);
        executing = true;
        var blocked = new BlockedPublishedRecords(publications, releases);
        return await ReleaseCommand.ExecuteAsync(args, publish, new ListPublicationReleases(publications, releases),
            new ListBlockedPublished(publications, releases, blocked),
            new ActivatePublicationRelease(publications, releases, blocked, loggers.CreateLogger<ActivatePublicationRelease>()), owner,
            Console.Out, Console.Error, cancellation.Token);
    }

    if (DocumentCommand.Matches(args))
    {
        var documentConfiguration = args is ["documents", "extract", var documentConfigPath, _, _]
            ? ConfigurationFile.ReadCollection(documentConfigPath) : null;
        var attempts = CreateDatabase();
        var extractions = PostgresDocumentExtractionStore.FromConnectionString(Environment.GetEnvironmentVariable("CIVIC_LENS_DATABASE")!);
        executing = true;
        return await DocumentCommand.ExecuteAsync(args, attempts, new CaptureDocumentTextExtractor(), extractions,
            cancellation.Token, documentConfiguration,
            PostgresDocumentHistoryStore.FromConnectionString(Environment.GetEnvironmentVariable("CIVIC_LENS_DATABASE")!),
            PostgresDocumentComparisonStore.FromConnectionString(Environment.GetEnvironmentVariable("CIVIC_LENS_DATABASE")!));
    }

    if (args is ["discovery", "get", var discoveryId])
    {
        if (!PendingCollectionHandoff.IsValidAttemptId(discoveryId)) throw new ArgumentException("Invalid attempt ID.");
        var evidence = CreateDatabase();
        executing = true;
        return await DiscoveryCommand.InspectAsync(evidence, discoveryId, cancellation.Token);
    }

    if (args is ["discovery", "admit", var discoveryConfigPath, var discoverySourceId, var discoveryAttemptId, var admissionKey])
    {
        var config = ConfigurationFile.ReadCollection(discoveryConfigPath);
        var source = new ConfiguredCollectionSource(config, discoverySourceId, admissionAsOf);
        var jobs = CreateJobs();
        executing = true;
        return await DiscoveryCommand.AdmitAsync(jobs, source, discoveryAttemptId, admissionKey, cancellation.Token);
    }

    if (args is ["jobs", "enqueue", var configPath, var sourceId, var idempotencyKey])
    {
        var config = ConfigurationFile.ReadCollection(configPath);
        var source = new ConfiguredCollectionSource(config, sourceId, admissionAsOf);
        var jobs = CreateJobs();
        executing = true;
        return await CollectionJobCommand.EnqueueAsync(jobs, source, idempotencyKey, cancellation.Token);
    }

    if (args[0] == "jobs")
    {
        CollectionJobCommand.ValidateArguments(args);
        var jobs = CreateJobs();
        executing = true;
        return await CollectionJobCommand.ExecuteAsync(args, jobs, cancellation.Token);
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

    var configuration = ConfigurationFile.ReadCollection(args[1]);
    if (args[0] == "validate")
    {
        Console.WriteLine($"Valid configuration: {configuration.People.Length} people, {configuration.Sources.Length} watched sources.");
        return 0;
    }

    var runner = new CollectorProcess(args[3], Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet");
    var attempt = new PreparedCollectionAttempt(new ConfiguredCollectionSource(configuration, args[2], coverageAsOf), args[4]);
    executing = true;
    var result = await new CollectWatchedPage(runner).ExecuteAsync(attempt, cancellation.Token);
    Console.WriteLine(JsonSerializer.Serialize(result, CollectionProtocol.JsonOptions));
    return result.Outcome is CollectionOutcome.Captured or CollectionOutcome.NotModified ? 0 : 1;
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine(replaying && executing
        ? "Receipt replay cancelled or timed out. Persistence was not confirmed. Use receipts list to inspect retained handoffs."
        : "Command cancelled or timed out.");
    return 1;
}
catch (RetiredSourceException exception) when (!executing)
{
    Console.Error.WriteLine(exception.Message);
    return 2;
}
catch (Exception exception) when (!executing && exception is ArgumentException or UnauthorizedAccessException or IOException or JsonException or InvalidOperationException)
{
    // Configuration and provider exceptions may contain credentials or other input values.
    Console.Error.WriteLine("Invalid input/configuration. Check command paths, source configuration, the 1 MB limit for configuration files, and CIVIC_LENS_DATABASE for database commands.");
    return 2;
}
catch (Exception)
{
    Console.Error.WriteLine(replaying
        ? "Receipt replay failed. Persistence was not confirmed. Check the capture directory, retained handoff, database connectivity, and applied migrations."
        : "Command failed. Check filesystem access, collector availability, or database connectivity as applicable.");
    return 1;
}

static ILoggerFactory CreateLoggers() =>
    LoggerFactory.Create(logging => logging.AddConsole(console => console.LogToStandardErrorThreshold = LogLevel.Trace));

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
