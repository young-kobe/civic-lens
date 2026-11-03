using System.Collections.Immutable;
using CivicLens.Application.Analysis;
using CivicLens.Application.Briefs;
using CivicLens.Application.Review;
using CivicLens.Core.Briefs;
using CivicLens.Core.Review;
using CivicLens.Infrastructure.Analysis;
using CivicLens.Infrastructure.Briefs;
using CivicLens.Infrastructure.Collection;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Application.Briefs;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using static CivicLens.Tests.Fixtures.BriefFixtures;

namespace CivicLens.Tests.Infrastructure.Briefs;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PostgresBriefCheckStoreTests(PostgresCollection postgres) : IAsyncLifetime
{
    private const string Note = "The paragraph says more than the record says.";
    private static readonly ReviewActor Reviewer = new("auth0|reviewer", ReviewRole.Reviewer);
    private static readonly ImmutableArray<BriefParagraph> PersonParagraphs = [new("A person paragraph.", ["R1", "R2", "R3"])];

    private string connectionString = null!;
    private PooledDbContextFactory<CollectionAttemptDbContext> factory = null!;
    private PostgresBriefReviewStore reviews = null!;
    private PostgresBriefDraftStore drafts = null!;
    private PostgresBriefCheckStore checks = null!;
    private BriefWorldSeeder seeder = null!;

    public async Task InitializeAsync()
    {
        connectionString = await postgres.CreateMigratedDatabaseAsync();
        factory = new PooledDbContextFactory<CollectionAttemptDbContext>(
            new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options);
        reviews = new PostgresBriefReviewStore(factory);
        drafts = new PostgresBriefDraftStore(factory);
        checks = new PostgresBriefCheckStore(factory);
        seeder = new BriefWorldSeeder(connectionString);
        await seeder.ReleaseCitedRecordsAsync();
        await seeder.ReviewableBriefAsync(ReviewableRevision());
    }

    public Task DisposeAsync() => postgres.DropDatabaseAsync(connectionString);

    [Fact]
    public async Task APersonRevisionQueuesACheckForItsWeekAndEachNewRevisionQueuesItAgain()
    {
        _ = await SaveAsync(1, "Person headline", "save-2");

        Assert.Equal($"Pending|0|{Week:yyyy-MM-dd}", await CheckRowAsync());
        Assert.Equal("Succeeded", await ScalarAsync("SELECT status FROM brief_analysis WHERE task = 'brief-draft'"));
        await seeder.ExecuteAsync("UPDATE brief_analysis SET status = 'Succeeded', attempts = 3 WHERE task = 'brief-check'");

        _ = await SaveAsync(2, "Second person headline", "save-3");

        Assert.Equal($"Pending|0|{Week:yyyy-MM-dd}", await CheckRowAsync());
    }

    [Fact]
    public async Task AWorkerRevisionQueuesACheckLikeAPersonRevision()
    {
        _ = await drafts.RedraftAsync(Week, default);
        var model = new FakeBriefDraftModel().Draft(Output("Model headline", paragraphs: [("A model paragraph.", ["R1", "R2", "R3"])]));

        await AnalysisWorkers.BriefDrafting(drafts, new PostgresAnalysisStatusStore(factory, BriefDraftTask.Task), model,
            new BriefDraftSettings(10_000_000, concurrency: 1), Officials, TimeProvider.System).ExecuteAsync(once: true);

        Assert.Equal(2, await ScalarAsync($"SELECT current_revision_number FROM brief_drafts WHERE record_id = '{RecordId}'"));
        Assert.Equal($"Pending|0|{Week:yyyy-MM-dd}", await CheckRowAsync());
    }

    [Fact]
    public async Task ACheckKeepsItsFlagsOnTheRevisionItCheckedAndChangesNothingInTheBriefOrItsReview()
    {
        await QueueCheckAsync();
        var before = await BriefStateAsync();

        await CheckAsync(CheckOutput("NeedsAttention", (1, "overstated", "R2", Note), (0, "other", null, Note)));

        Assert.Equal(before, await BriefStateAsync());
        Assert.Equal(0, await ScalarAsync("SELECT count(*)::int FROM brief_review_decisions"));
        Assert.Equal(["1|0|Other|", "1|1|Overstated|R2"], await RowsAsync("""
            SELECT r.checked_revision_number || '|' || f.paragraph_index || '|' || f.code || '|' || coalesce(f.citation_id, '')
            FROM brief_check_flags f JOIN brief_analysis_runs r ON r.run_id = f.run_id ORDER BY f.flag_number DESC
            """));
        Assert.Equal("1|NeedsAttention|brief-check", await ScalarAsync(
            "SELECT checked_revision_number || '|' || recommendation || '|' || task FROM brief_analysis_runs WHERE recommendation IS NOT NULL"));
    }

    [Fact]
    public async Task ApprovalStillWorksWhenTheCheckSaysNeedsAttentionBecauseTheCheckIsAdvice()
    {
        await QueueCheckAsync();
        await CheckAsync(CheckOutput("NeedsAttention", (0, "loadedWording", "R1", Note)));

        _ = await new DecideBriefReview(reviews).ExecuteAsync(Reviewer, new(RecordId, 1, 0, ReviewDecisionKind.Approve, null, [], "decide-1"));

        Assert.Equal("Approved", await ScalarAsync($"SELECT review_status FROM brief_drafts WHERE record_id = '{RecordId}'"));
    }

    [Fact]
    public async Task TwoSavesOfTheSameTextMakeOneCallAndTheSkippedRevisionPointsAtTheRunThatHoldsTheFlags()
    {
        var model = new FakeBriefCheckModel().Check(CheckOutput("NeedsAttention", (0, "other", null, Note)));
        _ = await SaveAsync(1, "Person headline", "save-2");
        await RunCheckWorkerAsync(model);
        var drafted = (string)(await ScalarAsync("SELECT run_id FROM brief_analysis_runs WHERE outcome = 'Drafted' AND task = 'brief-check'"))!;

        _ = await SaveAsync(2, "Person headline", "save-3");
        await RunCheckWorkerAsync(model);

        Assert.Single(model.Requests);
        Assert.Equal($"Bypassed|{drafted}", await ScalarAsync("SELECT outcome || '|' || source_run_id FROM brief_analysis_runs WHERE task = 'brief-check' AND outcome = 'Bypassed'"));
        Assert.Equal(1, await ScalarAsync($"SELECT count(*)::int FROM brief_check_flags WHERE run_id = '{drafted}'"));
        Assert.Equal("Succeeded|unchanged", await ScalarAsync("SELECT status || '|' || error_code FROM brief_analysis WHERE task = 'brief-check'"));
    }

    [Theory]
    [InlineData("Running", "lease_token = 'lease', lease_expires_at = 9000000000000000000, fence = 1")]
    [InlineData("Failed", "error_code = 'attemptsExhausted'")]
    public async Task ASaveAndApproveWithNoEditLeavesARunningAndAFailedCheckUntouched(string status, string columns)
    {
        var stored = await SaveAsync(1, "Person headline", "save-2");
        await seeder.ExecuteAsync($"UPDATE brief_analysis SET status = '{status}', attempts = 2, {columns} WHERE task = 'brief-check'");
        var before = await CheckStateAsync();

        var request = new SaveBriefDraftRequest(RecordId, 2, stored.Headline, PersonParagraphs, SuggestionChoice.Keep, "no-edit",
            new ApprovalWithSave(0, null, [], null));
        _ = await new SaveBriefDraft(reviews).ExecuteAsync(Reviewer, request);

        Assert.Equal(2, await ScalarAsync($"SELECT current_revision_number FROM brief_drafts WHERE record_id = '{RecordId}'"));
        Assert.Equal(before, await CheckStateAsync());
        Assert.StartsWith(status, before, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ANewRevisionDuringARunningCheckStoresNoFlagsAtLateSettlementAndTheNextPassChecksTheNewRevision()
    {
        await QueueCheckAsync();
        var revising = new RevisingModel(() => SaveAsync(1, "Person headline", "save-2"), CheckOutput("NeedsAttention", (0, "other", null, Note)));

        await RunCheckWorkerAsync(revising);

        Assert.Equal(0, await ScalarAsync("SELECT count(*)::int FROM brief_check_flags"));
        Assert.Equal(0, await ScalarAsync("SELECT count(*)::int FROM brief_analysis_runs WHERE recommendation IS NOT NULL"));
        Assert.StartsWith("Pending", await CheckRowAsync(), StringComparison.Ordinal);

        await CheckAsync(CheckOutput("NeedsAttention", (0, "overstated", "R1", Note)));

        Assert.Equal(["2|Overstated"], await RowsAsync("""
            SELECT r.checked_revision_number || '|' || f.code FROM brief_check_flags f JOIN brief_analysis_runs r ON r.run_id = f.run_id
            """));
    }

    [Fact]
    public async Task TheDraftAndTheCheckChargeTheirOwnRowsOfTheBudgetTable()
    {
        _ = await drafts.RedraftAsync(Week, default);
        var draftModel = new FakeBriefDraftModel().Draft(Output("Model headline", paragraphs: [("A model paragraph.", ["R1", "R2", "R3"])]));
        await AnalysisWorkers.BriefDrafting(drafts, new PostgresAnalysisStatusStore(factory, BriefDraftTask.Task), draftModel,
            new BriefDraftSettings(10_000_000, concurrency: 1), Officials, TimeProvider.System).ExecuteAsync(once: true);

        await RunCheckWorkerAsync(new FakeBriefCheckModel().Check(CheckOutput("ApproveRecommended")));

        Assert.Equal(["brief-check", "brief-draft"], await RowsAsync("SELECT task FROM brief_analysis_budget ORDER BY task"));
        Assert.Equal("1000", await ScalarAsync("SELECT charged_tokens::text FROM brief_analysis_budget WHERE task = 'brief-check'"));
        Assert.Equal("1000", await ScalarAsync("SELECT charged_tokens::text FROM brief_analysis_budget WHERE task = 'brief-draft'"));
    }

    [Fact]
    public async Task TheMigrationGoesDownWithCheckRowsOfEveryKindAndKeepsTheDraftRowsBecauseARollbackMustWork()
    {
        _ = await SaveAsync(1, "Person headline", "save-2");
        var model = new FakeBriefCheckModel().Check(CheckOutput("NeedsAttention", (0, "other", null, Note)));
        await RunCheckWorkerAsync(model);
        await seeder.ExecuteAsync("UPDATE brief_analysis SET status = 'Pending', attempts = 0 WHERE task = 'brief-check'");
        await RunCheckWorkerAsync(model);
        await seeder.ExecuteAsync("UPDATE brief_analysis SET retry_of_run_id = (SELECT run_id FROM brief_analysis_runs WHERE outcome = 'Drafted' AND task = 'brief-check') WHERE task = 'brief-check'");
        await new PostgresAnalysisStatusStore(factory, BriefCheckTask.Task).RecordStartAsync(10_000_000, default);
        await seeder.ExecuteAsync("""
            INSERT INTO brief_analysis_runs (run_id, record_id, task, task_version, model, prompt_version, schema_version, input_hash, outcome,
                input_tokens, output_tokens, cache_read_tokens, cache_write_tokens, charged_tokens, started_at_utc_ticks, finished_at_utc_ticks)
            SELECT repeat('d', 32), record_id, 'brief-draft', task_version, model, prompt_version, schema_version, input_hash, 'Bypassed',
                0, 0, 0, 0, 0, started_at_utc_ticks, finished_at_utc_ticks
            FROM brief_analysis_runs WHERE outcome = 'Drafted' AND task = 'brief-check'
            """);
        await seeder.ExecuteAsync("UPDATE brief_analysis_runs SET previous_run_id = (SELECT run_id FROM brief_analysis_runs WHERE outcome = 'Drafted' AND task = 'brief-check') WHERE outcome = 'Bypassed' AND task = 'brief-check'");

        await using (var db = new CollectionAttemptDbContext(new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options))
            await db.GetService<IMigrator>().MigrateAsync(db.Database.GetMigrations().TakeWhile(id => !id.EndsWith("_BriefCheck", StringComparison.Ordinal)).Last());

        foreach (var table in new[] { "brief_analysis", "brief_analysis_runs", "brief_analysis_budget", "document_change_analysis_status" })
            Assert.Equal(0, await ScalarAsync($"SELECT count(*)::int FROM {table} WHERE task = 'brief-check'"));
        Assert.Equal(1, await ScalarAsync("SELECT count(*)::int FROM brief_analysis WHERE task = 'brief-draft'"));
        Assert.Equal(1, await ScalarAsync("SELECT count(*)::int FROM brief_analysis_runs WHERE task = 'brief-draft'"));
    }

    [Fact]
    public async Task AnEditWithApprovalRequeuesASucceededCheckToPendingWithNoAttemptsAndKeepsOneRow()
    {
        _ = await SaveAsync(1, "Person headline", "save-2");
        await seeder.ExecuteAsync("UPDATE brief_analysis SET status = 'Succeeded', attempts = 3 WHERE task = 'brief-check'");
        var request = new SaveBriefDraftRequest(RecordId, 2, "Edited headline", PersonParagraphs, SuggestionChoice.Keep, "edit-and-approve",
            new ApprovalWithSave(0, null, [], null));

        _ = await new SaveBriefDraft(reviews).ExecuteAsync(Reviewer, request);

        Assert.Equal($"Pending|0|{Week:yyyy-MM-dd}", await CheckRowAsync());
        Assert.Equal(1, await ScalarAsync("SELECT count(*)::int FROM brief_analysis WHERE task = 'brief-check'"));
        Assert.Equal("Approved", await ScalarAsync($"SELECT review_status FROM brief_drafts WHERE record_id = '{RecordId}'"));
    }

    [Fact]
    public async Task ANewRevisionWithNewTextMakesANewCallAndItsFlagsBindToTheNewRevision()
    {
        await QueueCheckAsync();
        var model = new FakeBriefCheckModel().Check(CheckOutput("NeedsAttention", (0, "other", null, Note)))
            .Check(CheckOutput("NeedsAttention", (0, "overstated", "R1", Note)));
        await RunCheckWorkerAsync(model);

        _ = await SaveAsync(1, "Person headline", "save-2");
        await RunCheckWorkerAsync(model);

        Assert.Equal(2, model.Requests.Count);
        Assert.Equal(["1|Other", "2|Overstated"], await RowsAsync("""
            SELECT r.checked_revision_number || '|' || f.code FROM brief_check_flags f JOIN brief_analysis_runs r ON r.run_id = f.run_id
            ORDER BY r.checked_revision_number
            """));
    }

    [Fact]
    public async Task ARunCannotCheckARevisionThatDoesNotExist()
    {
        _ = await CheckedRunAsync();

        var failure = await Assert.ThrowsAsync<PostgresException>(() => seeder.ExecuteAsync(
            "UPDATE brief_analysis_runs SET checked_revision_number = 9 WHERE recommendation IS NOT NULL"));

        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, failure.SqlState);
    }

    [Theory]
    [InlineData(12, "Other", "A note.", "ck_brief_check_flags_paragraph")]
    [InlineData(-1, "Other", "A note.", "ck_brief_check_flags_paragraph")]
    [InlineData(0, "Bogus", "A note.", "ck_brief_check_flags_code")]
    [InlineData(0, "Other", "", "ck_brief_check_flags_message")]
    public async Task TheFlagTableRefusesAnOutOfRangeParagraphAnUnknownCodeAndAnEmptyNote(int paragraph, string code, string note, string constraint)
    {
        var runId = await CheckedRunAsync();

        var failure = await Assert.ThrowsAsync<PostgresException>(() => InsertFlagAsync(runId, 2, paragraph, code, note));

        Assert.Equal((PostgresErrorCodes.CheckViolation, constraint), (failure.SqlState, failure.ConstraintName));
    }

    [Fact]
    public async Task TheFlagTableRefusesANoteOverTheLimit()
    {
        var runId = await CheckedRunAsync();

        var failure = await Assert.ThrowsAsync<PostgresException>(() => InsertFlagAsync(runId, 2, paragraph: 0, code: "Other", note: new string('n', 301)));

        Assert.Equal(PostgresErrorCodes.StringDataRightTruncation, failure.SqlState);
    }

    [Fact]
    public async Task OnlyACheckRunMayCarryARecommendationBecauseADraftRunChecksNoRevision()
    {
        await QueueCheckAsync();
        await CheckAsync(CheckOutput("ApproveRecommended"));

        var failure = await Assert.ThrowsAsync<PostgresException>(() => seeder.ExecuteAsync(
            "UPDATE brief_analysis_runs SET task = 'brief-draft' WHERE recommendation IS NOT NULL"));

        Assert.Equal(("ck_brief_analysis_runs_check"), failure.ConstraintName);
    }

    [Fact]
    public async Task OnlyABypassedCheckRunMayNameASourceRun()
    {
        var runId = await CheckedRunAsync();

        var failure = await Assert.ThrowsAsync<PostgresException>(() => seeder.ExecuteAsync(
            "UPDATE brief_analysis_runs SET source_run_id = @run WHERE run_id = @run", ("run", runId)));

        Assert.Equal("ck_brief_analysis_runs_check", failure.ConstraintName);
    }

    [Theory]
    [InlineData("UPDATE brief_analysis_runs SET input_fingerprint = NULL WHERE recommendation IS NOT NULL")]
    [InlineData("UPDATE brief_analysis_runs SET input_fingerprint = 'abc' WHERE recommendation IS NOT NULL")]
    [InlineData("UPDATE brief_analysis_runs SET outcome = 'OutputRejected' WHERE recommendation IS NOT NULL")]
    [InlineData("UPDATE brief_analysis_runs SET task = 'brief-draft', checked_revision_number = NULL, recommendation = NULL WHERE recommendation IS NOT NULL")]
    public async Task OnlyADraftedCheckRunHasAFingerprintAndItIsAHash(string update)
    {
        _ = await CheckedRunAsync();

        var failure = await Assert.ThrowsAsync<PostgresException>(() => seeder.ExecuteAsync(update));

        Assert.Equal("ck_brief_analysis_runs_fingerprint", failure.ConstraintName);
    }

    [Fact]
    public async Task TheReadOnlyRoleCanReadTheModelNoteOfAFlagBecauseItIsNotAReviewerNote()
    {
        var hidden = ReadOnlyGrants.HiddenColumn();
        var columns = await RowsAsync("SELECT column_name FROM information_schema.columns WHERE table_name = 'brief_check_flags'");

        Assert.Contains("message", columns);
        Assert.DoesNotContain(columns, column => hidden.IsMatch(column));
        Assert.DoesNotMatch(hidden, "output_json");
        Assert.DoesNotMatch(hidden, "validation_json");
        Assert.DoesNotMatch(hidden, "source_run_id");
    }

    private async Task<string> CheckedRunAsync()
    {
        await QueueCheckAsync();
        await CheckAsync(CheckOutput("NeedsAttention", (0, "other", null, Note)));
        return (string)(await ScalarAsync("SELECT run_id FROM brief_analysis_runs WHERE recommendation IS NOT NULL"))!;
    }

    private Task InsertFlagAsync(string runId, int number, int paragraph, string code, string note) => seeder.ExecuteAsync("""
        INSERT INTO brief_check_flags (run_id, flag_number, paragraph_index, code, message)
        VALUES (@run, @number, @paragraph, @code, @note)
        """, ("run", runId), ("number", number), ("paragraph", paragraph), ("code", code), ("note", note));

    private Task QueueCheckAsync() => seeder.ExecuteAsync("""
        INSERT INTO brief_analysis (record_id, task, week_start, status, fence, attempts, reserved_tokens, created_at_utc_ticks)
        VALUES (@id, 'brief-check', @week, 'Pending', 0, 0, 0, 1)
        """, ("id", RecordId), ("week", Week));

    private Task CheckAsync(string output) => RunCheckWorkerAsync(new FakeBriefCheckModel().Check(output));

    private Task RunCheckWorkerAsync(IBriefCheckModel model) =>
        AnalysisWorkers.BriefChecking(checks, new PostgresAnalysisStatusStore(factory, BriefCheckTask.Task), model,
            new BriefCheckSettings(10_000_000, concurrency: 1), TimeProvider.System).ExecuteAsync(once: true);

    private Task<BriefRevision> SaveAsync(int expected, string headline, string key) =>
        new SaveBriefDraft(reviews).ExecuteAsync(Reviewer, new(RecordId, expected, headline, PersonParagraphs, SuggestionChoice.Keep, key));

    private async Task<string> CheckStateAsync() => (string)(await ScalarAsync(
        "SELECT status || '|' || fence || '|' || attempts || '|' || coalesce(error_code, '') || '|' || coalesce(lease_token, '') FROM brief_analysis WHERE task = 'brief-check'"))!;

    private async Task<string> CheckRowAsync() => (string)(await ScalarAsync(
        "SELECT status || '|' || attempts || '|' || week_start FROM brief_analysis WHERE task = 'brief-check'"))!;

    private async Task<string> BriefStateAsync() => (string)(await ScalarAsync($"""
        SELECT d.current_revision_number || '|' || d.review_state_version || '|' || d.review_status || '|' || (SELECT count(*) FROM brief_revisions)
        FROM brief_drafts d WHERE d.record_id = '{RecordId}'
        """))!;

    private async Task<object?> ScalarAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return await command.ExecuteScalarAsync();
    }

    private async Task<List<string>> RowsAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<string>();
        while (await reader.ReadAsync()) rows.Add(reader.GetString(0));
        return rows;
    }

    private sealed class RevisingModel(Func<Task> revise, string output) : IBriefCheckModel
    {
        public async Task<AnalysisModelResponse> CheckAsync(AnalysisModelRequest request, CancellationToken cancellationToken)
        {
            await revise();
            return new("end_turn", output, new(900, 100, 0, 0), "req_fixture");
        }
    }

    private static string CheckOutput(string recommendation, params (int Paragraph, string Code, string? CitationId, string Note)[] flags) =>
        FakeBriefCheckModel.Output(recommendation, flags);
}
