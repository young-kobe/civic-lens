using System.Text.Json;
using CivicLens.Application.Issues;
using CivicLens.Application.Publication;
using CivicLens.Application.Review;
using CivicLens.Host.Publication;
using CivicLens.Infrastructure.Actions;
using CivicLens.Infrastructure.Briefs;
using CivicLens.Infrastructure.Publication;
using CivicLens.Infrastructure.Relationships;
using CivicLens.Infrastructure.Review;
using CivicLens.Infrastructure.Statements;
using CivicLens.Infrastructure.Votes;
using CivicLens.Publication.Contracts;
using CivicLens.Application.Analysis;
using CivicLens.Application.ExecutiveActions;
using CivicLens.Core.Review;
using CivicLens.Infrastructure.Analysis;
using CivicLens.Infrastructure.ExecutiveActions;
using CivicLens.Tests.Infrastructure.Collection;
using CivicLens.Tests.Application.Analysis;
using CivicLens.Tests.Fixtures;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.ExecutiveActions;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PostgresExecutiveSummaryTests(PostgresCollection postgres) : IAsyncLifetime
{
    private const string Quote = "I hereby order the Secretary of Commerce to review each import rule within 30 days";
    private const string Sentence = "The order tells the Secretary of Commerce to review each import rule. The review must finish within 30 days.";
    private const string Second = "The Secretary must report the results to the President by March 1, 2025.";
    private const string SecondQuote = "The Secretary shall report the results to the President by March 1, 2025";
    private static readonly string Body = $"By the authority vested in me as President, {Quote}.\n{SecondQuote}.\nEach agency head shall help.";
    private readonly string captureRoot = Path.Combine(Path.GetTempPath(), "executive-summary-" + Guid.NewGuid().ToString("N"));
    private string connectionString = null!;
    private ExecutiveActionSeeder seeder = null!;

    public async Task InitializeAsync()
    {
        connectionString = await postgres.CreateMigratedDatabaseAsync();
        seeder = new ExecutiveActionSeeder(connectionString, captureRoot);
    }

    public async Task DisposeAsync()
    {
        await postgres.DropDatabaseAsync(connectionString);
        Directory.Delete(captureRoot, recursive: true);
    }

    [Fact]
    public async Task SavingTheTextAddsExactlyOneQueueEntryInTheSameTransactionAndADocumentWithNoTextHasNone()
    {
        _ = await seeder.AdmitAsync(FederalRegisterFixtures.TrumpOrder, "donald-trump");
        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM exec_summary_analysis"));

        _ = await seeder.AdmitWithTextAsync(FederalRegisterFixtures.BidenProclamation, "joe-biden", Body);

        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM exec_summary_analysis WHERE status = 'Pending' AND task = 'executive-summary'"));
    }

    [Fact]
    public async Task TheWorkerAppendsTheSummaryAsTheNextRevisionWithItsRunAndCodeApprovesThatRevisionNumber()
    {
        var (recordId, textId) = await seeder.AdmitWithTextAsync(FederalRegisterFixtures.BidenProclamation, "joe-biden", Body);

        await RunWorkerAsync(new FakeExecutiveSummaryModel().Draft(Output()));

        Assert.Equal((2, "Approved"), ((int)(await ScalarAsync("SELECT current_revision_number FROM executive_action_drafts"))!,
            (string)(await ScalarAsync("SELECT review_status FROM executive_action_drafts"))!));
        Assert.Equal(("Analysis", "civic-lens:analysis", false, "Model"), await RevisionAsync(recordId, 2));
        var runId = (string)(await ScalarAsync($"SELECT run_id FROM executive_action_revisions WHERE record_id = '{recordId}' AND revision_number = 2"))!;
        Assert.Equal("Drafted", await ScalarAsync($"SELECT outcome FROM exec_summary_analysis_runs WHERE run_id = '{runId}'"));
        Assert.Equal(DBNull.Value, await ScalarAsync($"SELECT run_id FROM executive_action_revisions WHERE record_id = '{recordId}' AND revision_number = 1"));
        Assert.Equal(("civic-lens:code", "Approve"), await DecisionAsync(recordId, 2));
        Assert.Equal(2L, await ScalarAsync($"SELECT count(*) FROM executive_action_revision_summary_sentences WHERE quote_extraction_id = '{textId}'"));
        Assert.Equal(("Succeeded", DBNull.Value), (await ScalarAsync("SELECT status FROM exec_summary_analysis"), await ScalarAsync("SELECT error_code FROM exec_summary_analysis")));
        var review = (await ReviewStore().GetAsync(recordId, default))!.Review;
        var first = review.CurrentRevision.Summary!.Sentences[0].Quote;
        Assert.Equal(Quote, (await TextOf(textId)).Substring(first.Start, first.Length));
    }

    [Fact]
    public async Task ASecondRunOfTheSameRecordSettlesUnchangedWithNoCallAndNoRevision()
    {
        var (recordId, _) = await seeder.AdmitWithTextAsync(FederalRegisterFixtures.BidenProclamation, "joe-biden", Body);
        await RunWorkerAsync(new FakeExecutiveSummaryModel().Draft(Output()));
        _ = await ExecuteAsync($"UPDATE exec_summary_analysis SET status = 'Pending', error_code = NULL WHERE record_id = '{recordId}'");
        var model = new FakeExecutiveSummaryModel();

        await RunWorkerAsync(model);

        Assert.Empty(model.Requests);
        Assert.Equal(2, await ScalarAsync("SELECT current_revision_number FROM executive_action_drafts"));
        Assert.Equal("unchanged", await ScalarAsync("SELECT error_code FROM exec_summary_analysis"));
    }

    [Fact]
    public async Task ARevisionWhoseCutFlagDiffersFromTheStoredCountsIsNotApprovedByCodeAndKeepsItsReason()
    {
        _ = await seeder.AdmitWithTextAsync(FederalRegisterFixtures.BidenProclamation, "joe-biden", Body);
        var model = new FakeExecutiveSummaryModel().Reply(async request =>
        {
            _ = await ExecuteAsync("UPDATE executive_action_texts SET chars_total = 70000");
            return new AnalysisModelResponse("end_turn", Output(), new(900, 100, 0, 0), "req_fixture");
        });

        await RunWorkerAsync(model);

        Assert.Equal((2, "AwaitingReview"), ((int)(await ScalarAsync("SELECT current_revision_number FROM executive_action_drafts"))!,
            (string)(await ScalarAsync("SELECT review_status FROM executive_action_drafts"))!));
        Assert.Contains("cut flag", (string)(await ScalarAsync("SELECT held_reason FROM executive_action_drafts"))!, StringComparison.Ordinal);
        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM executive_action_review_decisions WHERE revision_number = 2"));
    }

    [Fact]
    public async Task ARevisionThatQuotesAnExtractionOtherThanTheStoredTextIsNotApprovedByCode()
    {
        var (_, textId) = await seeder.AdmitWithTextAsync(FederalRegisterFixtures.BidenProclamation, "joe-biden", Body);
        var other = (string)(await ScalarAsync($"SELECT extraction_id FROM document_extractions WHERE extraction_id <> '{textId}' LIMIT 1"))!;
        var model = new FakeExecutiveSummaryModel().Reply(async request =>
        {
            _ = await ExecuteAsync($"UPDATE executive_action_texts SET extraction_id = '{other}'");
            return new AnalysisModelResponse("end_turn", Output(), new(900, 100, 0, 0), "req_fixture");
        });

        await RunWorkerAsync(model);

        Assert.Equal("AwaitingReview", await ScalarAsync("SELECT review_status FROM executive_action_drafts"));
        Assert.Contains("not the stored text", (string)(await ScalarAsync("SELECT held_reason FROM executive_action_drafts"))!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARejectedRecordGetsNoSummaryRevisionAndItsRunIsDiscarded()
    {
        var (recordId, _) = await seeder.AdmitWithTextAsync(FederalRegisterFixtures.BidenProclamation, "joe-biden", Body);
        var model = new FakeExecutiveSummaryModel().Reply(async request =>
        {
            _ = await ExecuteAsync($"UPDATE executive_action_drafts SET review_status = 'Rejected' WHERE record_id = '{recordId}'");
            return new AnalysisModelResponse("end_turn", Output(), new(900, 100, 0, 0), "req_fixture");
        });

        await RunWorkerAsync(model);

        Assert.Equal(1, await ScalarAsync("SELECT current_revision_number FROM executive_action_drafts"));
        Assert.Equal("DraftDiscarded", await ScalarAsync("SELECT outcome FROM exec_summary_analysis_runs"));
        Assert.Equal("recordRejected", await ScalarAsync("SELECT error_code FROM exec_summary_analysis"));
    }

    [Fact]
    public async Task PublicationReadsTheApprovedSummaryWithTheExtractionOfItsQuotes()
    {
        var (recordId, textId) = await seeder.AdmitWithTextAsync(FederalRegisterFixtures.BidenProclamation, "joe-biden", Body);
        await RunWorkerAsync(new FakeExecutiveSummaryModel().Draft(Output()));

        var publishable = (await ReviewStore().GetForPublicationAsync([recordId], default))[recordId];

        Assert.Equal(2, publishable.Review.CurrentRevision.Summary!.Sentences.Length);
        Assert.Contains(textId, publishable.Extractions.Keys);
    }

    [Fact]
    public async Task APublishedReleaseCarriesTheSummaryWithItsQuotesAndTheTextAsASourceOfTheRecord()
    {
        var (recordId, textId) = await seeder.AdmitWithTextAsync(FederalRegisterFixtures.BidenProclamation, "joe-biden", Body);
        await RunWorkerAsync(new FakeExecutiveSummaryModel().Draft(Output()));

        var release = await PublishAsync(recordId);

        var record = JsonSerializer.Deserialize<PublishedExecutiveAction>(
            File.ReadAllBytes(Directory.EnumerateFiles(Path.Combine(captureRoot, "releases"), recordId + ".json", SearchOption.AllDirectories).First()),
            PublicationProtocol.JsonOptions)!;
        Assert.Equal(1, release.RecordCount);
        Assert.Equal([Quote, SecondQuote], record.Summary!.Sentences.Select(sentence => sentence.Citation.Quote));
        Assert.False(record.Summary.Cut);
        Assert.Contains(textId, record.Sources.Select(source => source.ExtractionId));
    }

    [Fact]
    public async Task PublishingStopsAndWritesNoReleaseWhenTheSummaryQuotesAnotherExtraction()
    {
        var (recordId, textId) = await seeder.AdmitWithTextAsync(FederalRegisterFixtures.BidenProclamation, "joe-biden", Body);
        await RunWorkerAsync(new FakeExecutiveSummaryModel().Draft(Output()));
        var other = (string)(await ScalarAsync($"SELECT extraction_id FROM document_extractions WHERE extraction_id <> '{textId}' LIMIT 1"))!;
        _ = await ExecuteAsync($"UPDATE executive_action_texts SET extraction_id = '{other}'");

        await Assert.ThrowsAsync<ArgumentException>(() => PublishAsync(recordId));

        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM publication_releases"));
    }

    private async Task<PublicationReleaseSummary> PublishAsync(string recordId)
    {
        var factory = AnalysisDbContextFactory.Create(connectionString);
        var store = PostgresPublicationStore.FromConnectionString(connectionString, TestCodeVersion.Current);
        var directory = new FileReleaseDirectory(Path.Combine(captureRoot, "releases"));
        var publish = new PublishRelease(store, directory, new ReleaseRenderer(null, null),
            new PublishedRecordLoaders(new PostgresDocumentChangeReviewStore(factory),
                new PostgresStatementReviewStore(factory), new PostgresActionReviewStore(factory), new PostgresVoteReviewStore(factory),
                new PostgresRelationshipReviewStore(factory), new PostgresBriefReviewStore(factory), ReviewStore()),
            new PublicationCatalog(new Dictionary<string, string> { ["joe-biden"] = "Joe Biden" }, IssueConfiguration.None, IssueIconConfiguration.None),
            FixedRosters.NamedOnly(["joe-biden"]), PhotoCaptures.None(), TimeProvider.System,
            new PruneReleases(store, directory, NullLogger<PruneReleases>.Instance));
        return await publish.ExecuteAsync(new ReviewActor("auth0|owner", ReviewRole.Owner),
            new PublishReleaseRequest([new(PublishedRecordKind.ExecutiveAction, recordId)], "publish"));
    }

    [Fact]
    public async Task PublicationStopsWhenTheSummaryQuotesAnExtractionThatIsNotTheStoredTextOfItsDocument()
    {
        var (recordId, textId) = await seeder.AdmitWithTextAsync(FederalRegisterFixtures.BidenProclamation, "joe-biden", Body);
        await RunWorkerAsync(new FakeExecutiveSummaryModel().Draft(Output()));
        var other = (string)(await ScalarAsync($"SELECT extraction_id FROM document_extractions WHERE extraction_id <> '{textId}' LIMIT 1"))!;
        _ = await ExecuteAsync($"UPDATE executive_action_texts SET extraction_id = '{other}'");

        var stop = await Assert.ThrowsAsync<ArgumentException>(() => ReviewStore().GetForPublicationAsync([recordId], default));

        Assert.Contains("not the stored text", stop.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PublicationStopsWhenTheCutFlagOfTheSummaryDiffersFromTheStoredCounts()
    {
        var (recordId, _) = await seeder.AdmitWithTextAsync(FederalRegisterFixtures.BidenProclamation, "joe-biden", Body);
        await RunWorkerAsync(new FakeExecutiveSummaryModel().Draft(Output()));
        _ = await ExecuteAsync("UPDATE executive_action_texts SET chars_total = 70000");

        var stop = await Assert.ThrowsAsync<ArgumentException>(() => ReviewStore().GetForPublicationAsync([recordId], default));

        Assert.Contains("cut flag", stop.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheMigrationQueuesTheTextsThatExistedBeforeItAndRollsBackToTheCodeOnlyAuthorRule()
    {
        _ = await seeder.AdmitWithTextAsync(FederalRegisterFixtures.BidenProclamation, "joe-biden", Body);
        await using var db = new CivicLens.Infrastructure.Collection.CollectionAttemptDbContext(
            new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<CivicLens.Infrastructure.Collection.CollectionAttemptDbContext>().UseNpgsql(connectionString).Options);
        var migrator = Microsoft.EntityFrameworkCore.Infrastructure.AccessorExtensions.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>(db);
        var before = db.Database.GetMigrations().TakeWhile(id => !id.EndsWith("_ExecutiveActionSummary", StringComparison.Ordinal)).Last();
        _ = await ExecuteAsync("DELETE FROM exec_summary_analysis");

        await migrator.MigrateAsync(before);
        await migrator.MigrateAsync();

        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM exec_summary_analysis WHERE status = 'Pending'"));
        await migrator.MigrateAsync(before);
        Assert.Equal(DBNull.Value, await ScalarAsync("SELECT to_regclass('exec_summary_analysis')::text"));
        Assert.Equal(DBNull.Value, await ScalarAsync("SELECT to_regclass('executive_action_revision_summary_sentences')::text"));
    }

    private static string Output() => JsonSerializer.Serialize(new
    {
        summary = new { sentences = new[] { new { sentence = Sentence, quote = Quote }, new { sentence = Second, quote = SecondQuote } }, confidence = 0.9 }
    });

    private async Task<string> TextOf(string extractionId) => (string)(await ScalarAsync($"SELECT text FROM document_extractions WHERE extraction_id = '{extractionId}'"))!;

    private PostgresExecutiveActionReviewStore ReviewStore() => PostgresExecutiveActionReviewStore.FromConnectionString(connectionString, TestCodeVersion.Current);

    private async Task RunWorkerAsync(FakeExecutiveSummaryModel model)
    {
        var store = new PostgresExecutiveSummaryStore(AnalysisDbContextFactory.Create(connectionString), TestCodeVersion.Current);
        var step = new ExecutiveSummaryStep(store, model, new ExecutiveSummarySettings(10_000_000, 1));
        await new AnalysisWorker<ExecutiveSummaryRun>(step, new FakeAnalysisStatusStore(), TimeProvider.System,
            NullLogger<AnalysisWorker<ExecutiveSummaryRun>>.Instance, TestBeat.Create()).ExecuteAsync(once: true);
    }

    private async Task<(string, string, bool, string)> RevisionAsync(string recordId, int number)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"SELECT author_kind, author_subject, summary_cut, summary_text_origin FROM executive_action_revisions WHERE record_id = '{recordId}' AND revision_number = {number}", connection);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (reader.GetString(0), reader.GetString(1), reader.GetBoolean(2), reader.GetString(3));
    }

    private async Task<(string, string)> DecisionAsync(string recordId, int number)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"SELECT actor_subject, kind FROM executive_action_review_decisions WHERE record_id = '{recordId}' AND revision_number = {number}", connection);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (reader.GetString(0), reader.GetString(1));
    }

    private async Task<object?> ScalarAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return await command.ExecuteScalarAsync();
    }

    private async Task<int> ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return await command.ExecuteNonQueryAsync();
    }
}
