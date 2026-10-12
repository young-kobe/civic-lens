using System.Text.Json;
using CivicLens.Application.Analysis;
using CivicLens.Application.Collection.Jobs;
using CivicLens.Application.Statements;
using CivicLens.Core.Review;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Documents;
using CivicLens.Infrastructure.Statements;
using CivicLens.Tests.Application.Analysis;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;
using static CivicLens.Tests.Fixtures.StatementDraftFixtures;
using static CivicLens.Tests.Fixtures.StatementGateFixtures;

namespace CivicLens.Tests.Infrastructure.Statements;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PostgresStatementDraftStoreTests(PostgresCollection postgres) : IAsyncLifetime
{
    private static readonly ReviewActor Owner = new("auth0|owner", ReviewRole.Owner);
    private readonly string schema = "draft_" + Guid.NewGuid().ToString("N");
    private readonly string captureRoot = Path.Combine(Path.GetTempPath(), "civic-draft-" + Guid.NewGuid().ToString("N"));
    private string connectionString = null!;
    private PostgresStatementAnalysisStore gates = null!;
    private PostgresStatementDraftStore drafts = null!;
    private StatementCandidateSeeder seeder = null!;

    public async Task InitializeAsync()
    {
        await ExecuteAsync(postgres.ConnectionString, $"CREATE SCHEMA {schema}");
        connectionString = new NpgsqlConnectionStringBuilder(postgres.ConnectionString) { SearchPath = schema, Pooling = false }.ConnectionString;
        var factory = new PooledDbContextFactory<CollectionAttemptDbContext>(
            new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options);
        var attempts = new PostgresCollectionAttemptStore(factory);
        await attempts.MigrateAsync();
        gates = new PostgresStatementAnalysisStore(factory);
        drafts = new PostgresStatementDraftStore(factory);
        seeder = new StatementCandidateSeeder(attempts, new PostgresDocumentExtractionStore(factory), connectionString, captureRoot);
        Directory.CreateDirectory(captureRoot);
    }

    public async Task DisposeAsync()
    {
        await ExecuteAsync(postgres.ConnectionString, $"DROP SCHEMA IF EXISTS {schema} CASCADE");
        Directory.Delete(captureRoot, recursive: true);
    }

    [Fact]
    public async Task AValidDraftWritesTheDraftTheAiRevisionAndTheRunInOneTransactionWithTheGateIssuesAndItsOwnBudget()
    {
        var candidate = await PassedCandidateAsync();
        var model = new FakeStatementDraftingModel().Draft(Output(quotes: ["a rent cap for tenants"]));

        await DraftingWorker(model).ExecuteAsync(once: true);

        Assert.Equal([(StatementDraftTask.Task, "Succeeded", null)], await QueueAsync(candidate, StatementDraftTask.Task));
        Assert.Equal(1L, await ScalarAsync($"SELECT current_revision_number FROM statement_drafts WHERE candidate_id = '{candidate}'"));
        var runId = await TextAsync($"SELECT run_id FROM statement_analysis_runs WHERE candidate_id = '{candidate}' AND task = '{StatementDraftTask.Task}'");
        Assert.Equal(("Analysis", "civic-lens:analysis", runId), await RevisionAsync(candidate));
        var revision = JsonSerializer.Deserialize<JsonElement>(await TextAsync($"SELECT revision_json FROM statement_revisions WHERE candidate_id = '{candidate}'"));
        Assert.Equal(RentIssue, revision.GetProperty("issueIds")[0].GetString());
        Assert.Equal(HousingArea, revision.GetProperty("policyAreaIds")[0].GetString());
        Assert.Equal(1_000L, await ScalarAsync($"SELECT charged_tokens FROM statement_analysis_budget WHERE task = '{StatementDraftTask.Task}'"));
        Assert.Equal(1_000L, await ScalarAsync("SELECT charged_tokens FROM statement_analysis_budget WHERE task = 'gate'"));
        var run = (await drafts.GetRunAsync(runId, default))!;
        Assert.Equal(model.Requests[0].InputHash, run.Run.InputHash);
        Assert.Null(await gates.GetRunAsync(runId, default));
    }

    [Fact]
    public async Task AnArticleOverTheByteCapGetsADraftWithoutARevisionAndABypassedRun()
    {
        var candidate = await PassedCandidateAsync(new string('x', 6_000), "https://example.test/pages/big", gateSettings: BigGate);
        var model = new FakeStatementDraftingModel();

        await DraftingWorker(model, new StatementDraftSettings(10_000_000, StatementDraftPrompt.MinimumReservation, 1)).ExecuteAsync(once: true);

        Assert.Empty(model.Requests);
        Assert.Equal([(StatementDraftTask.Task, "Succeeded", "over-byte-cap")], await QueueAsync(candidate, StatementDraftTask.Task));
        Assert.Equal(0L, await ScalarAsync($"SELECT current_revision_number FROM statement_drafts WHERE candidate_id = '{candidate}'"));
        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM statement_revisions"));
        Assert.Equal(1L, await ScalarAsync($"SELECT count(*) FROM statement_analysis_runs WHERE task = '{StatementDraftTask.Task}' AND outcome = 'Bypassed' AND reason = 'over-byte-cap' AND charged_tokens = 0"));
    }

    [Fact]
    public async Task ARepeatRunAndARequeueOfASucceededEntryChangeNothing()
    {
        _ = await PassedCandidateAsync();
        await DraftingWorker(new FakeStatementDraftingModel().Draft(Output())).ExecuteAsync(once: true);
        var again = new FakeStatementDraftingModel();

        await DraftingWorker(again).ExecuteAsync(once: true);

        Assert.Empty(again.Requests);
        Assert.Equal(0, await new RequeueAnalyses<StatementDraftRun>(drafts).ExecuteAsync(Owner, null));
        Assert.Equal((1L, 1L, 1L), (await ScalarAsync("SELECT count(*) FROM statement_drafts"), await ScalarAsync("SELECT count(*) FROM statement_revisions"),
            await ScalarAsync($"SELECT count(*) FROM statement_analysis_runs WHERE task = '{StatementDraftTask.Task}'")));
    }

    [Fact]
    public async Task ADraftThatAlreadyExistsSucceedsTheEntryWithoutACall()
    {
        var candidate = await PassedCandidateAsync();
        await InsertDraftAsync(candidate);
        var model = new FakeStatementDraftingModel();

        await DraftingWorker(model).ExecuteAsync(once: true);

        Assert.Empty(model.Requests);
        Assert.Equal([(StatementDraftTask.Task, "Succeeded", "humanDraftExists")], await QueueAsync(candidate, StatementDraftTask.Task));
    }

    [Fact]
    public async Task ADraftThatAppearsDuringTheCallKeepsTheRunAsDiscardedAndAddsNoSecondDraft()
    {
        var candidate = await PassedCandidateAsync();
        var model = new FakeStatementDraftingModel().Reply(async _ =>
        {
            await InsertDraftAsync(candidate);
            return new AnalysisModelResponse("end_turn", Output(), new(900, 100, 0, 0), "req_fixture");
        });

        await DraftingWorker(model).ExecuteAsync(once: true);

        Assert.Equal([(StatementDraftTask.Task, "Succeeded", "draftDiscarded")], await QueueAsync(candidate, StatementDraftTask.Task));
        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM statement_revisions"));
        Assert.Equal(1L, await ScalarAsync($"SELECT count(*) FROM statement_analysis_runs WHERE task = '{StatementDraftTask.Task}' AND outcome = 'DraftDiscarded'"));
    }

    [Fact]
    public async Task TwoRejectedAnswersFailTheEntryAndAnOwnerRequeueRunsItAgainWithoutTouchingTheGate()
    {
        var candidate = await PassedCandidateAsync();
        var model = new FakeStatementDraftingModel().Draft("not json").Draft(Output(quotes: ["a rent freeze"]));
        await DraftingWorker(model).ExecuteAsync(once: true);
        Assert.Equal([("gate", "Succeeded", "passed"), (StatementDraftTask.Task, "Failed", "citationRejected")], await QueueAsync(candidate));
        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM statement_drafts"));

        var requeued = await new RequeueAnalyses<StatementDraftRun>(drafts).ExecuteAsync(Owner, candidate);
        await DraftingWorker(new FakeStatementDraftingModel().Draft(Output())).ExecuteAsync(once: true);

        Assert.Equal(1, requeued);
        Assert.Equal([("gate", "Succeeded", "passed"), (StatementDraftTask.Task, "Succeeded", null)], await QueueAsync(candidate));
        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM statement_revisions"));
    }

    [Fact]
    public async Task ListAndCountSeparateEachDraftOutcome()
    {
        _ = await PassedCandidateAsync(Article + " one", "https://example.test/pages/one");
        _ = await PassedCandidateAsync(Article + " two", "https://example.test/pages/two");
        var model = new FakeStatementDraftingModel().Draft(Output(quotes: ["a rent cap for tenants"])).Fail(AnalysisModelFailure.RateLimited);
        await DraftingWorker(model).ExecuteAsync(once: true);

        var counts = await drafts.CountAsync(default);
        var drafted = await new ListStatementAnalyses<StatementDraftRun, StatementDraftView, StatementDraftListItem>(drafts).ExecuteAsync(Owner, StatementDraftView.Drafted, 10);

        Assert.Equal((1, 1, 0), (counts[StatementDraftView.Drafted], counts[StatementDraftView.Pending], counts[StatementDraftView.Failed]));
        Assert.Equal((null, "source"), (Assert.Single(drafted).Code, drafted[0].SourceId));
    }

    [Fact]
    public async Task TheDatabaseRefusesAnAiRevisionThatHasNoRun()
    {
        var candidate = await PassedCandidateAsync();
        await InsertDraftAsync(candidate);

        var failure = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(connectionString, $"""
            INSERT INTO statement_revisions (candidate_id, revision_number, author_subject, author_kind, run_id, revision_json, created_at_utc_ticks)
            VALUES ('{candidate}', 1, 'civic-lens:analysis', 'Analysis', NULL, 'x', 0)
            """));

        Assert.Equal("ck_statement_revisions_run", failure.ConstraintName);
    }

    [Fact]
    public async Task EachCandidateIsDraftedForTheSpeakersOfTheConfigurationThatAdmittedIt()
    {
        var admittedForMarkey = AdmittedDefinition(SoleCoverage(Markey));
        var admittedForWarren = AdmittedDefinition(SoleCoverage(Warren));
        var markey = await PassedCandidateAsync(Article + " one", "https://example.test/pages/one", admittedBy: admittedForMarkey);
        var warren = await PassedCandidateAsync(Article + " two", "https://example.test/pages/two", admittedBy: admittedForWarren);
        var model = new FakeStatementDraftingModel().Draft(Output()).Draft(Output());

        await DraftingWorker(model).ExecuteAsync(once: true);

        Assert.Equal(admittedForMarkey.ConfigurationRevision, (await drafts.ReadEvidenceAsync(markey, default))!.ConfigurationRevision);
        Assert.Equal(admittedForWarren.ConfigurationRevision, (await drafts.ReadEvidenceAsync(warren, default))!.ConfigurationRevision);
        Assert.Equal([Markey], await SpeakersAsync(markey));
        Assert.Equal([Warren], await SpeakersAsync(warren));
    }

    [Fact]
    public async Task ACandidateAdmittedUnderTheSameRevisionReusesTheRevisionTheStoreAlreadyReadAndChecked()
    {
        var first = await PassedCandidateAsync(Article + " one", "https://example.test/pages/one");
        var second = await PassedCandidateAsync(Article + " two", "https://example.test/pages/two");

        var firstRevision = (await drafts.ReadEvidenceAsync(first, default))!.ConfigurationRevision;
        var secondRevision = (await drafts.ReadEvidenceAsync(second, default))!.ConfigurationRevision;

        Assert.NotNull(firstRevision);
        Assert.Same(firstRevision, secondRevision);
    }

    [Fact]
    public async Task AJobThatHasNoConfigurationRevisionBlocksTheDraftWithoutACall()
    {
        var candidate = await PassedCandidateAsync(admittedBy: LegacyDefinition());
        var model = new FakeStatementDraftingModel();

        await DraftingWorker(model).ExecuteAsync(once: true);

        Assert.Empty(model.Requests);
        Assert.Equal([(StatementDraftTask.Task, "Blocked", "speakersUnavailable")], await QueueAsync(candidate, StatementDraftTask.Task));
    }

    private StatementGateSettings BigGate => new(10_000_000, StatementGatePrompt.MinimumReservation, 1);

    private async Task<string> PassedCandidateAsync(string article = Article, string url = "https://example.test/pages/a",
        StatementGateSettings? gateSettings = null, CollectionJobDefinition? admittedBy = null)
    {
        var candidate = await seeder.SeedAsync(article, url, admittedBy);
        var gateModel = new FakeStatementGateModel();
        if (gateSettings is null) gateModel.Classify(StatementGateFixtures.Output([HousingArea], 0.8, (RentIssue, "rent cap")));
        await AnalysisWorkers.Gate(gates, new FakeAnalysisStatusStore(), gateModel, gateSettings ?? new StatementGateSettings(10_000_000, concurrency: 1),
            Revision(), TimeProvider.System).ExecuteAsync(once: true);
        return candidate;
    }

    private AnalysisWorker<StatementDraftRun> DraftingWorker(FakeStatementDraftingModel model, StatementDraftSettings? settings = null) =>
        AnalysisWorkers.StatementDrafting(drafts, new FakeAnalysisStatusStore(), model, settings ?? new StatementDraftSettings(10_000_000, concurrency: 1),
            TimeProvider.System);

    private Task InsertDraftAsync(string candidate) => ExecuteAsync(connectionString,
        $"INSERT INTO statement_drafts (candidate_id, current_revision_number, created_at_utc_ticks) VALUES ('{candidate}', 0, 0)");

    private async Task<List<(string Task, string Status, string? Code)>> QueueAsync(string candidate, string? task = null)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"""
            SELECT task, status, error_code FROM statement_analysis
             WHERE candidate_id = '{candidate}' AND (CAST(@task AS text) IS NULL OR task = @task) ORDER BY task
            """, connection);
        command.Parameters.AddWithValue("task", (object?)task ?? DBNull.Value);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<(string, string, string?)>();
        while (await reader.ReadAsync()) rows.Add((reader.GetString(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2)));
        return rows;
    }

    private async Task<(string Kind, string Subject, string? RunId)> RevisionAsync(string candidate)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            $"SELECT author_kind, author_subject, run_id FROM statement_revisions WHERE candidate_id = '{candidate}' AND revision_number = 1", connection);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (reader.GetString(0), reader.GetString(1), reader.GetString(2));
    }

    private async Task<string[]> SpeakersAsync(string candidate)
    {
        var revision = JsonSerializer.Deserialize<JsonElement>(await TextAsync($"SELECT revision_json FROM statement_revisions WHERE candidate_id = '{candidate}'"));
        return [.. revision.GetProperty("speakerOfficialIds").EnumerateArray().Select(id => id.GetString()!)];
    }

    private async Task<long> ScalarAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private async Task<string> TextAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private static async Task ExecuteAsync(string connection, string sql)
    {
        await using var open = new NpgsqlConnection(connection);
        await open.OpenAsync();
        await using var command = new NpgsqlCommand(sql, open);
        await command.ExecuteNonQueryAsync();
    }
}
