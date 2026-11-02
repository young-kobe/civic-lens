using System.Collections.Immutable;
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
        Assert.Equal(["1|0|Other|", "1|1|Overstated|R2"], await RowsAsync(
            "SELECT revision_number || '|' || paragraph_index || '|' || code || '|' || coalesce(citation_id, '') FROM brief_check_flags ORDER BY flag_number DESC"));
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
    public async Task ACheckOfTheSameInputMakesNoSecondCallAndKeepsTheFirstFlags()
    {
        await QueueCheckAsync();
        var model = new FakeBriefCheckModel().Check(CheckOutput("NeedsAttention", (0, "other", null, Note)));
        await RunCheckWorkerAsync(model);
        await seeder.ExecuteAsync("UPDATE brief_analysis SET status = 'Pending', attempts = 0 WHERE task = 'brief-check'");

        await RunCheckWorkerAsync(model);

        Assert.Single(model.Requests);
        Assert.Equal(1, await ScalarAsync("SELECT count(*)::int FROM brief_check_flags"));
        Assert.Equal("Succeeded|unchanged", await ScalarAsync("SELECT status || '|' || error_code FROM brief_analysis WHERE task = 'brief-check'"));
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
        Assert.Equal(["1|Other", "2|Overstated"], await RowsAsync(
            "SELECT revision_number || '|' || code FROM brief_check_flags ORDER BY revision_number"));
    }

    [Fact]
    public async Task AFlagOfARevisionThatDoesNotExistIsRefusedByTheForeignKey()
    {
        var runId = await CheckedRunAsync();

        var failure = await Assert.ThrowsAsync<PostgresException>(() => InsertFlagAsync(runId, 2, revision: 9, paragraph: 0, code: "Other", note: Note));

        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, failure.SqlState);
    }

    [Theory]
    [InlineData(12, "Other", "A note.", "ck_brief_check_flags_paragraph")]
    [InlineData(-1, "Other", "A note.", "ck_brief_check_flags_paragraph")]
    [InlineData(0, "Bogus", "A note.", "ck_brief_check_flags_code")]
    [InlineData(0, "Other", "", "ck_brief_check_flags_note")]
    public async Task TheFlagTableRefusesAnOutOfRangeParagraphAnUnknownCodeAndAnEmptyNote(int paragraph, string code, string note, string constraint)
    {
        var runId = await CheckedRunAsync();

        var failure = await Assert.ThrowsAsync<PostgresException>(() => InsertFlagAsync(runId, 2, revision: 1, paragraph, code, note));

        Assert.Equal((PostgresErrorCodes.CheckViolation, constraint), (failure.SqlState, failure.ConstraintName));
    }

    [Fact]
    public async Task TheFlagTableRefusesANoteOverTheLimit()
    {
        var runId = await CheckedRunAsync();

        var failure = await Assert.ThrowsAsync<PostgresException>(() => InsertFlagAsync(runId, 2, revision: 1, paragraph: 0, code: "Other", note: new string('n', 301)));

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
    public async Task TheNoteOfAFlagIsHiddenFromTheReadOnlyRoleAndTheStructuralColumnsAreNot()
    {
        var hidden = ReadOnlyGrants.HiddenColumnPattern();
        var columns = await RowsAsync("SELECT column_name FROM information_schema.columns WHERE table_name = 'brief_check_flags'");

        Assert.Equal(["note"], columns.Where(column => hidden.IsMatch(column)));
        Assert.Contains("recommendation", await RowsAsync("SELECT column_name FROM information_schema.columns WHERE table_name = 'brief_analysis_runs'"));
        Assert.DoesNotMatch(hidden, "recommendation");
        Assert.DoesNotMatch(hidden, "checked_revision_number");
    }

    private async Task<string> CheckedRunAsync()
    {
        await QueueCheckAsync();
        await CheckAsync(CheckOutput("NeedsAttention", (0, "other", null, Note)));
        return (string)(await ScalarAsync("SELECT run_id FROM brief_analysis_runs WHERE recommendation IS NOT NULL"))!;
    }

    private Task InsertFlagAsync(string runId, int number, int revision, int paragraph, string code, string note) => seeder.ExecuteAsync("""
        INSERT INTO brief_check_flags (run_id, flag_number, record_id, revision_number, paragraph_index, code, note)
        VALUES (@run, @number, @id, @revision, @paragraph, @code, @note)
        """, ("run", runId), ("number", number), ("id", RecordId), ("revision", revision), ("paragraph", paragraph), ("code", code), ("note", note));

    private Task QueueCheckAsync() => seeder.ExecuteAsync("""
        INSERT INTO brief_analysis (record_id, task, week_start, status, fence, attempts, reserved_tokens, created_at_utc_ticks)
        VALUES (@id, 'brief-check', @week, 'Pending', 0, 0, 0, 1)
        """, ("id", RecordId), ("week", Week));

    private Task CheckAsync(string output) => RunCheckWorkerAsync(new FakeBriefCheckModel().Check(output));

    private Task RunCheckWorkerAsync(FakeBriefCheckModel model) =>
        AnalysisWorkers.BriefChecking(checks, new PostgresAnalysisStatusStore(factory, BriefCheckTask.Task), model,
            new BriefCheckSettings(10_000_000, concurrency: 1), TimeProvider.System).ExecuteAsync(once: true);

    private Task<BriefRevision> SaveAsync(int expected, string headline, string key) =>
        new SaveBriefDraft(reviews).ExecuteAsync(Reviewer, new(RecordId, expected, headline, PersonParagraphs, SuggestionChoice.Keep, key));

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

    private static string CheckOutput(string recommendation, params (int Paragraph, string Code, string? CitationId, string Note)[] flags) =>
        FakeBriefCheckModel.Output(recommendation, flags);
}
