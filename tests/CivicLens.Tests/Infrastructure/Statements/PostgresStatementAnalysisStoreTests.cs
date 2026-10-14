using CivicLens.Application.Analysis;
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
using static CivicLens.Tests.Fixtures.StatementGateFixtures;

namespace CivicLens.Tests.Infrastructure.Statements;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PostgresStatementAnalysisStoreTests(PostgresCollection postgres) : IAsyncLifetime
{
    private const string Article = "The senator announced a rent cap for tenants.";
    private static readonly ReviewActor Owner = new("auth0|owner", ReviewRole.Owner);
    private readonly string captureRoot = Path.Combine(Path.GetTempPath(), "civic-gate-" + Guid.NewGuid().ToString("N"));
    private string connectionString = null!;
    private PostgresStatementAnalysisStore store = null!;
    private StatementCandidateSeeder seeder = null!;

    public async Task InitializeAsync()
    {
        connectionString = await postgres.CreateMigratedDatabaseAsync();
        var factory = new PooledDbContextFactory<CollectionAttemptDbContext>(
            new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options);
        var attempts = new PostgresCollectionAttemptStore(factory);
        store = new PostgresStatementAnalysisStore(factory);
        seeder = new StatementCandidateSeeder(attempts, new PostgresDocumentExtractionStore(factory), connectionString, captureRoot);
        Directory.CreateDirectory(captureRoot);
    }

    public async Task DisposeAsync()
    {
        await postgres.DropDatabaseAsync(connectionString);
        Directory.Delete(captureRoot, recursive: true);
    }

    [Fact]
    public async Task APassSettlesTheGateAndInsertsExactlyOnePendingDraftRowInTheSameTransaction()
    {
        var candidate = await seeder.SeedAsync(Article, "https://example.test/pages/a");
        var model = new FakeStatementGateModel().Classify(Output([HousingArea], 0.8, (RentIssue, "a rent cap")));

        await Worker(model, Revision()).ExecuteAsync(once: true);

        Assert.Equal([("gate", "Succeeded", "passed"), (StatementDraftTask.Task, "Pending", null)], await RowsAsync(candidate));
        var run = (await store.GetRunAsync(await RunIdAsync(candidate), default))!;
        Assert.Equal((Revision().Id, StatementGateReasons.Passed), (run.IssueRevisionId, run.Reason));
        Assert.Equal(Article.IndexOf("a rent cap", StringComparison.Ordinal), Assert.Single(run.Quotes).Start);
        Assert.Contains(HousingArea, run.Run.OutputJson, StringComparison.Ordinal);
        Assert.Equal(model.Requests[0].InputHash, run.Run.InputHash);
    }

    [Fact]
    public async Task ASkipInsertsNoDraftRowAndKeepsTheReasonAndRubricRevisionOnTheRun()
    {
        var candidate = await seeder.SeedAsync(Article, "https://example.test/pages/a");
        var model = new FakeStatementGateModel().Classify(Output([HealthArea]));

        await Worker(model, Revision()).ExecuteAsync(once: true);

        Assert.Equal([("gate", "Succeeded", "no-tracked-issue")], await RowsAsync(candidate));
        var run = (await store.GetRunAsync(await RunIdAsync(candidate), default))!;
        Assert.Equal((Revision().Id, StatementGateReasons.NoTrackedIssue), (run.IssueRevisionId, run.Reason));
    }

    [Fact]
    public async Task TheGateChargesItsOwnDailyBudgetAndNotTheDocumentChangeBudget()
    {
        _ = await seeder.SeedAsync(Article, "https://example.test/pages/a");
        var model = new FakeStatementGateModel().Classify(Output([]));

        await Worker(model, Revision()).ExecuteAsync(once: true);

        Assert.Equal(1_000L, await store.GetChargedTodayAsync(default));
        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM document_change_analysis_budget"));
    }

    [Fact]
    public async Task OverTheByteCapRecordsABypassRunWithoutChargingAndInsertsTheDraftRow()
    {
        var candidate = await seeder.SeedAsync(new string('x', 6_000), "https://example.test/pages/big");
        var model = new FakeStatementGateModel();

        await Worker(model, Revision(), new StatementGateSettings(10_000_000, StatementGatePrompt.MinimumReservation, 1)).ExecuteAsync(once: true);

        Assert.Empty(model.Requests);
        Assert.Equal([("gate", "Succeeded", "over-byte-cap"), (StatementDraftTask.Task, "Pending", null)], await RowsAsync(candidate));
        Assert.Equal(0L, await store.GetChargedTodayAsync(default));
    }

    [Fact]
    public async Task TwoInvalidAnswersFailOpenAndKeepBothFailedRuns()
    {
        var candidate = await seeder.SeedAsync(Article, "https://example.test/pages/a");
        var model = new FakeStatementGateModel().Classify("not json").Classify(Output([], 0.8, (RentIssue, "a rent freeze")));

        await Worker(model, Revision()).ExecuteAsync(once: true);

        Assert.Equal([("gate", "Succeeded", "gate-failed-open"), (StatementDraftTask.Task, "Pending", null)], await RowsAsync(candidate));
        Assert.Equal(2L, await ScalarAsync("SELECT count(*) FROM statement_analysis_runs WHERE outcome IN ('OutputRejected','CitationRejected')"));
        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM statement_analysis_runs WHERE reason = 'gate-failed-open'"));
    }

    [Fact]
    public async Task RegateRequeuesOnlySkippedCandidatesBoundToAnOlderRevision()
    {
        var skippedOld = await seeder.SeedAsync(Article + " one", "https://example.test/pages/one");
        var skippedOlder = await seeder.SeedAsync(Article + " two", "https://example.test/pages/two");
        var passed = await seeder.SeedAsync(Article + " three", "https://example.test/pages/three");
        var model = new FakeStatementGateModel().Classify(Output([])).Classify(Output([])).Classify(Output([], 0.8, (RentIssue, "a rent cap")));
        await Worker(model, Revision()).ExecuteAsync(once: true);
        var current = Revision("Rules on eviction.");

        var requeued = await new RegateStatementCandidates(store).ExecuteAsync(Owner, current.Id, null);

        Assert.Equal(2, requeued);
        Assert.Equal([("gate", "Pending", null)], await RowsAsync(skippedOld));
        Assert.Equal([("gate", "Pending", null)], await RowsAsync(skippedOlder));
        Assert.Equal(("gate", "Succeeded", "passed"), (await RowsAsync(passed))[0]);
        var again = new FakeStatementGateModel().Classify(Output([])).Classify(Output([]));
        await Worker(again, current).ExecuteAsync(once: true);
        Assert.Equal(0, await new RegateStatementCandidates(store).ExecuteAsync(Owner, current.Id, null));
    }

    [Fact]
    public async Task RegateWithASinceDateSkipsCandidatesCapturedBeforeIt()
    {
        _ = await seeder.SeedAsync(Article, "https://example.test/pages/a");
        await Worker(new FakeStatementGateModel().Classify(Output([])), Revision()).ExecuteAsync(once: true);
        var current = Revision("Rules on eviction.");

        var tomorrow = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));
        var yesterday = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));

        Assert.Equal(0, await new RegateStatementCandidates(store).ExecuteAsync(Owner, current.Id, tomorrow));
        Assert.Equal(1, await new RegateStatementCandidates(store).ExecuteAsync(Owner, current.Id, yesterday));
    }

    [Fact]
    public async Task GateClaimsStayOldestFirstEvenWhenTheNewerCandidateHasTheSmallerIdSoNoCandidateWaitsBehindNewerWork()
    {
        var one = await seeder.SeedAsync(Article + " one", "https://example.test/pages/one");
        var two = await seeder.SeedAsync(Article + " two", "https://example.test/pages/two");
        var (older, newer) = string.CompareOrdinal(one, two) > 0 ? (one, two) : (two, one);
        await ExecuteAsync(connectionString,
            $"UPDATE statement_analysis SET created_at_utc_ticks = CASE candidate_id WHEN '{older}' THEN 1 ELSE 2 END");

        Assert.Equal([older, newer], (await store.GetEligibleAsync(10, default)).Select(row => row.SubjectId));
    }

    [Fact]
    public async Task ListAndCountSeparateEachGateOutcome()
    {
        _ = await seeder.SeedAsync(Article + " one", "https://example.test/pages/one");
        _ = await seeder.SeedAsync(Article + " two", "https://example.test/pages/two");
        _ = await seeder.SeedAsync(Article + " three", "https://example.test/pages/three");
        var model = new FakeStatementGateModel().Classify(Output([])).Classify(Output([], 0.8, (RentIssue, "a rent cap")))
            .Fail(CivicLens.Application.Analysis.AnalysisModelFailure.RateLimited);
        await Worker(model, Revision()).ExecuteAsync(once: true);

        var counts = await store.CountAsync(default);
        var skipped = await new ListStatementAnalyses<StatementGateRun, StatementGateView, StatementGateListItem>(store).ExecuteAsync(Owner, StatementGateView.Skipped, 10);

        Assert.Equal(1, counts[StatementGateView.Skipped]);
        Assert.Equal(1, counts[StatementGateView.Passed]);
        Assert.Equal(1, counts[StatementGateView.Pending]);
        Assert.Equal(StatementGateReasons.NoTrackedIssue, Assert.Single(skipped).Code);
        Assert.Equal(Revision().Id, Assert.Single(skipped).IssueRevisionId);
    }

    private AnalysisWorker<StatementGateRun> Worker(FakeStatementGateModel model, CivicLens.Application.Issues.IssueConfigurationRevision revision,
        StatementGateSettings? settings = null) =>
        AnalysisWorkers.Gate(store, new FakeAnalysisStatusStore(), model, settings ?? new StatementGateSettings(10_000_000, concurrency: 1),
            revision, TimeProvider.System);

    private async Task<string> RunIdAsync(string candidate) => await ScalarTextAsync(
        $"SELECT run_id FROM statement_analysis_runs WHERE candidate_id = '{candidate}' ORDER BY started_at_utc_ticks DESC LIMIT 1");

    private async Task<List<(string Task, string Status, string? Code)>> RowsAsync(string candidate)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            $"SELECT task, status, error_code FROM statement_analysis WHERE candidate_id = '{candidate}' ORDER BY task", connection);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<(string, string, string?)>();
        while (await reader.ReadAsync()) rows.Add((reader.GetString(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2)));
        return rows;
    }

    private async Task<long> ScalarAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private async Task<string> ScalarTextAsync(string sql)
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
