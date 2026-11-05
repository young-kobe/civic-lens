using System.Text.Json;
using CivicLens.Application.Publication;
using CivicLens.Application.Review;
using CivicLens.Core.Briefs;
using CivicLens.Core.Review;
using CivicLens.Infrastructure.Briefs;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Review;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;
using System.Data.Common;

namespace CivicLens.Tests.Infrastructure.Briefs;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PostgresBriefRevisionColumnsTests(PostgresCollection postgres) : IAsyncLifetime
{
    private static readonly ReviewActor Owner = new("auth0|owner", ReviewRole.Owner);
    private static readonly string SuggestionRunId = new('a', 32);
    private static readonly BriefRevision WithSuggestion = BriefFixtures.ReviewableRevision() with
    {
        Suggestion = new("Another headline", [new("Another text.", ["R1"])],
            [new("R1", BriefCitationKind.Statement, BriefFixtures.CitedStatement, 1)], SuggestionRunId, new string('b', 64))
    };

    private string connectionString = null!;
    private BriefWorldSeeder seeder = null!;

    public async Task InitializeAsync()
    {
        connectionString = await postgres.CreateMigratedDatabaseAsync();
        seeder = new BriefWorldSeeder(connectionString);
    }

    public Task DisposeAsync() => postgres.DropDatabaseAsync(connectionString);

    [Fact]
    public async Task ALegacyRevisionMovesToColumnsThatEqualItsJsonAndTheJsonStaysUntouched()
    {
        await SeedAsync();
        await seeder.MakeLegacyAsync();
        var jsonBefore = await ScalarAsync("SELECT revision_json FROM brief_revisions");

        var result = await BackfillAsync(10);

        Assert.Equal((1, 1, 0), (result.Examined, result.Moved, result.Skipped));
        Assert.Equal(jsonBefore, await ScalarAsync("SELECT revision_json FROM brief_revisions"));
        Assert.Equal(4L, await ScalarAsync("SELECT count(*) FROM brief_revision_citations"));
        Assert.Equal(3L, await ScalarAsync("SELECT count(*) FROM brief_revision_paragraphs"));
        Assert.Equal(4L, await ScalarAsync("SELECT count(*) FROM brief_revision_paragraph_citations"));
        var verification = await VerifyAsync();
        Assert.Equal((1, 0, 0), (verification.Checked, verification.Legacy, verification.Mismatched));
        Assert.Equal(JsonSerializer.Serialize(WithSuggestion, JsonSerializerOptions.Web),
            JsonSerializer.Serialize(await Store().GetRevisionAsync(WithSuggestion.RecordId, 1, default), JsonSerializerOptions.Web));
    }

    [Fact]
    public async Task ARerunChangesNothingBecauseTheBackfillTouchesOnlyRowsWithNullColumns()
    {
        await SeedAsync();
        await seeder.MakeLegacyAsync();
        await BackfillAsync(10);
        var snapshot = await SnapshotAsync();

        var rerun = await BackfillAsync(10);

        Assert.Equal((0, 0, 0), (rerun.Examined, rerun.Moved, rerun.Skipped));
        Assert.Equal(snapshot, await SnapshotAsync());
    }

    [Theory]
    [InlineData("x", "UnreadableJson")]
    [InlineData("null", "UnreadableJson")]
    [InlineData("{}", "InvalidShape")]
    public async Task JsonThatIsNoRevisionIsSkippedWithAReasonAndTheRowStaysAsItWas(string json, string reason)
    {
        await SeedAsync();
        await seeder.MakeLegacyAsync();
        await ExecuteAsync("UPDATE brief_revisions SET revision_json = @json", ("json", json));

        var result = await BackfillAsync(10);

        Assert.Equal((1, 0, 1), (result.Examined, result.Moved, result.Skipped));
        Assert.Equal(reason, Assert.Single(result.SkippedRevisions).Reason);
        Assert.Equal(json, await ScalarAsync("SELECT revision_json FROM brief_revisions"));
        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM brief_revision_citations"));
    }

    [Fact]
    public async Task AnOldRevisionThatFailsTheShapeRulesOfTodayIsSkippedAndNeverChanged()
    {
        await SeedAsync();
        await seeder.MakeLegacyAsync();
        await ExecuteAsync("UPDATE brief_revisions SET revision_json = jsonb_set(revision_json::jsonb, '{paragraphs,0,citationIds}', '[\"R7\"]')::text");

        var result = await BackfillAsync(10);

        Assert.Equal("InvalidShape", Assert.Single(result.SkippedRevisions).Reason);
        Assert.Null(await ScalarAsync("SELECT headline FROM brief_revisions"));
    }

    [Fact]
    public async Task ACitationOfARevisionThatIsNotStoredIsSkippedAndNothingIsGuessedBecauseAForeignKeyRefusesIt()
    {
        await SeedAsync();
        await seeder.MakeLegacyAsync();
        await ExecuteAsync("UPDATE brief_revisions SET revision_json = jsonb_set(revision_json::jsonb, '{citations,0,revisionNumber}', '9')::text");

        var result = await BackfillAsync(10);

        var skipped = Assert.Single(result.SkippedRevisions);
        Assert.StartsWith("database:", skipped.Reason, StringComparison.Ordinal);
        Assert.Contains("brief_revision_citations", skipped.Reason, StringComparison.Ordinal);
        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM brief_revision_citations"));
    }

    [Fact]
    public async Task TheBackfillWaitsForTheBriefReviewLockBecauseAWriterOfTheSameKeyMustNeverSeeAHalfMovedBatch()
    {
        await SeedAsync();
        await seeder.MakeLegacyAsync();
        await using var held = await HeldAdvisoryLock.AcquireAsync(connectionString, "civic-lens-brief-review");

        var running = BackfillAsync(10);
        await HeldAdvisoryLock.WaitForWaiterAsync(connectionString, "civic-lens-brief-review");
        Assert.False(running.IsCompleted);
        await held.DisposeAsync();

        Assert.Equal(1, (await running).Moved);
    }

    [Fact]
    public async Task VerifyReportsALegacyRowAsLegacyAndNotAsAMismatch()
    {
        await SeedAsync();
        await seeder.MakeLegacyAsync();

        var verification = await VerifyAsync();

        Assert.Equal((0, 1, 0), (verification.Checked, verification.Legacy, verification.Mismatched));
    }

    [Theory]
    [InlineData("UPDATE brief_revisions SET headline = 'Private changed headline'", "headline")]
    [InlineData("UPDATE brief_revisions SET fingerprint = repeat('e', 64)", "fingerprint")]
    [InlineData("UPDATE brief_revisions SET text_origin = 'Code'", "textOrigin")]
    [InlineData("UPDATE brief_revisions SET first_release_number = first_release_number + 1", "window")]
    [InlineData("UPDATE brief_revisions SET coverage_considered = coverage_considered + 1", "coverage")]
    [InlineData("UPDATE brief_revisions SET suggestion_headline = 'Private suggestion'", "suggestion")]
    [InlineData("UPDATE brief_revision_paragraphs SET paragraph_text = 'Private changed text' WHERE part = 'Text' AND position = 0", "paragraphs")]
    [InlineData("DELETE FROM brief_revision_paragraph_citations WHERE part = 'Text' AND paragraph_position = 0", "paragraphs")]
    [InlineData("UPDATE brief_revision_citations SET statement_revision_number = 5 WHERE kind = 'Statement' AND part = 'Text'", "citations")]
    [InlineData("UPDATE brief_revision_paragraphs SET paragraph_text = 'Private changed text' WHERE part = 'Suggestion'", "suggestion")]
    public async Task VerifyFailsWhenAColumnDiffersFromTheJsonAndNamesTheFieldButNeverItsText(string change, string field)
    {
        await SeedAsync();
        await ExecuteAsync("SET session_replication_role = replica;\n" + change);

        var verification = await VerifyAsync();

        Assert.True(verification.Mismatched >= 1);
        Assert.Contains(field, verification.FirstMismatches.SelectMany(mismatch => mismatch.Fields));
        Assert.DoesNotContain("private", JsonSerializer.Serialize(verification), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task VerifyReportsAnUnreadableJsonAsAMismatchOfTheJsonAndWritesNothing()
    {
        await SeedAsync();
        await ExecuteAsync("UPDATE brief_revisions SET revision_json = 'x'");
        var snapshot = await SnapshotAsync();

        var mismatch = Assert.Single((await VerifyAsync()).FirstMismatches);

        Assert.Equal(["revisionJson"], mismatch.Fields);
        Assert.Equal(snapshot, await SnapshotAsync());
    }

    [Fact]
    public async Task AReaderFailsOnARowWithNullColumnsAndTheJsonIsNoFallback()
    {
        await SeedAsync();
        await seeder.MakeLegacyAsync();

        var thrown = await Assert.ThrowsAsync<InvalidDataException>(() => Store().GetRevisionAsync(WithSuggestion.RecordId, 1, default));

        Assert.Contains("Run the brief revision backfill", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AReaderBuildsTheRevisionFromTheColumnsAndNotFromTheJson()
    {
        await SeedAsync();
        await ExecuteAsync("UPDATE brief_revisions SET headline = 'Column headline', revision_json = jsonb_set(revision_json::jsonb, '{headline}', '\"Json headline\"')::text");

        var revision = await Store().GetRevisionAsync(WithSuggestion.RecordId, 1, default);

        Assert.Equal("Column headline", revision!.Headline);
    }

    [Fact]
    public async Task ACitationThatNoParagraphCitesFailsTheReadBecauseNoForeignKeyCanProveThatRule()
    {
        await SeedAsync();
        await ExecuteAsync($"""
            SET session_replication_role = replica;
            INSERT INTO brief_revision_citations (record_id, revision_number, part, citation_id, position, kind, statement_candidate_id, statement_revision_number)
            VALUES ('{WithSuggestion.RecordId}', 1, 'Text', 'R9', 9, 'Statement', repeat('f', 64), 1)
            """);

        var thrown = await Assert.ThrowsAsync<ArgumentException>(() => Store().GetRevisionAsync(WithSuggestion.RecordId, 1, default));

        Assert.Contains("Every listed citation is cited", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheHeadlineSearchFailsOnARowWithNullColumnsBecauseItMustNotSkipTheRow()
    {
        await SeedAsync("Approved");
        await seeder.MakeLegacyAsync();

        var thrown = await Assert.ThrowsAsync<InvalidDataException>(() => QueueStore().SearchApprovedAsync("housing", null, 10, default));

        Assert.Contains("brief", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheHeadlineSearchReadsTheHeadlineColumnAndNotTheJson()
    {
        await SeedAsync("Approved");
        await ExecuteAsync("UPDATE brief_revisions SET revision_json = jsonb_set(revision_json::jsonb, '{headline}', '\"Zebra json\"')::text");

        var hits = await QueueStore().SearchApprovedAsync("zebra", null, 10, default);
        var titled = await QueueStore().SearchApprovedAsync("housing", null, 10, default);

        Assert.Empty(hits.Items);
        Assert.Equal(WithSuggestion.RecordId, Assert.Single(titled.Items).RecordId);
    }

    [Fact]
    public async Task ReadingMoreBriefsCostsTheSameNumberOfQueriesBecauseNoReaderQueriesEachRow()
    {
        var counter = new QueryCounter();
        var counted = new PostgresBriefReviewStore(new PooledDbContextFactory<CollectionAttemptDbContext>(
            new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).AddInterceptors(counter).Options));
        await seeder.ReleaseCitedRecordsAsync();
        var first = BriefFixtures.ReviewableRevision();
        await seeder.ReviewableBriefAsync(first);
        await counted.GetForPublicationAsync([first.RecordId], [], default);
        counter.Count = 0;
        await counted.GetForPublicationAsync([first.RecordId], [], default);
        var queriesForOne = counter.Count;
        var weeks = Enumerable.Range(1, 3).Select(offset => BriefFixtures.Week.AddDays(-7 * offset)).ToArray();
        foreach (var week in weeks) await seeder.ReviewableBriefAsync(BriefFixtures.ReviewableRevision(week));
        counter.Count = 0;

        var read = await counted.GetForPublicationAsync([first.RecordId, .. weeks.Select(BriefWeek.RecordIdOf)], [], default);

        Assert.Equal(4, read.Count);
        Assert.Equal(queriesForOne, counter.Count);
    }

    [Theory]
    [InlineData("UPDATE brief_revisions SET fingerprint = 'not-hex'", "ck_brief_revisions_fingerprint")]
    [InlineData("UPDATE brief_revisions SET headline = ' '", "ck_brief_revisions_headline")]
    [InlineData("UPDATE brief_revisions SET text_origin = 'Robot'", "ck_brief_revisions_text_origin")]
    [InlineData("UPDATE brief_revisions SET author_kind = 'Person', run_id = NULL", "ck_brief_revisions_person_text")]
    [InlineData("UPDATE brief_revisions SET window_end_utc_ticks = window_start_utc_ticks + 10", "ck_brief_revisions_window")]
    [InlineData("UPDATE brief_revisions SET cutoff_release_number = 0", "ck_brief_revisions_window")]
    [InlineData("UPDATE brief_revisions SET coverage_sent_to_model = coverage_considered + 1", "ck_brief_revisions_coverage")]
    [InlineData("UPDATE brief_revisions SET headline = NULL", "ck_brief_revisions_columns_written")]
    [InlineData("UPDATE brief_revisions SET suggestion_run_id = NULL", "ck_brief_revisions_suggestion")]
    [InlineData("UPDATE brief_revisions SET suggestion_fingerprint = 'short'", "ck_brief_revisions_suggestion")]
    [InlineData("UPDATE brief_revision_paragraphs SET paragraph_text = ' ' WHERE position = 0 AND part = 'Text'", "ck_brief_revision_paragraphs_text")]
    [InlineData("UPDATE brief_revision_paragraphs SET position = 12 WHERE position = 1 AND part = 'Text'", "ck_brief_revision_paragraphs_position")]
    [InlineData("UPDATE brief_revision_citations SET citation_id = 'R0' WHERE citation_id = 'R1' AND part = 'Text'", "ck_brief_revision_citations_id")]
    [InlineData("UPDATE brief_revision_citations SET statement_revision_number = NULL WHERE kind = 'Statement' AND part = 'Text'", "ck_brief_revision_citations_cited")]
    [InlineData("UPDATE brief_revision_citations SET kind = 'Action' WHERE kind = 'Statement' AND part = 'Text'", "ck_brief_revision_citations_cited")]
    [InlineData("UPDATE brief_revision_citations SET statement_revision_number = 0 WHERE kind = 'Statement' AND part = 'Text'", "ck_brief_revision_citations_cited")]
    [InlineData("UPDATE brief_revision_citations SET position = 256 WHERE citation_id = 'R2' AND part = 'Text'", "ck_brief_revision_citations_position")]
    public async Task TheDatabaseRefusesARowThatBreaksARule(string change, string constraint)
    {
        await SeedAsync();

        var thrown = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(change));

        Assert.Equal(constraint, thrown.ConstraintName);
    }

    [Theory]
    [InlineData("INSERT INTO brief_revision_citations (record_id, revision_number, part, citation_id, position, kind, statement_candidate_id, statement_revision_number) SELECT record_id, revision_number, part, 'R9', 9, kind, statement_candidate_id, statement_revision_number FROM brief_revision_citations WHERE kind = 'Statement' AND part = 'Text'", PostgresErrorCodes.UniqueViolation)]
    [InlineData("UPDATE brief_revision_citations SET statement_revision_number = 99 WHERE kind = 'Statement' AND part = 'Text'", PostgresErrorCodes.ForeignKeyViolation)]
    [InlineData("UPDATE brief_revision_citations SET action_revision_number = 99 WHERE kind = 'Action'", PostgresErrorCodes.ForeignKeyViolation)]
    [InlineData("UPDATE brief_revision_citations SET document_change_revision_number = 99 WHERE kind = 'DocumentChange' AND part = 'Text'", PostgresErrorCodes.ForeignKeyViolation)]
    [InlineData("INSERT INTO brief_revision_paragraph_citations (record_id, revision_number, part, paragraph_position, position, citation_id) SELECT record_id, revision_number, part, 0, 5, 'R9' FROM brief_revisions CROSS JOIN (SELECT 'Text' AS part) p", PostgresErrorCodes.ForeignKeyViolation)]
    [InlineData("UPDATE brief_revision_paragraph_citations SET citation_id = 'R2' WHERE part = 'Text' AND paragraph_position = 1 AND position = 1", PostgresErrorCodes.UniqueViolation)]
    [InlineData("UPDATE brief_revisions SET week_start = week_start + 7", PostgresErrorCodes.ForeignKeyViolation)]
    [InlineData("UPDATE brief_revisions SET suggestion_run_id = repeat('c', 32)", PostgresErrorCodes.ForeignKeyViolation)]
    [InlineData("DELETE FROM brief_revisions", PostgresErrorCodes.RestrictViolation)]
    [InlineData("DELETE FROM brief_revision_paragraphs WHERE position = 0 AND part = 'Text'", PostgresErrorCodes.RestrictViolation)]
    public async Task TheDatabaseRefusesALinkThatBreaksAKey(string change, string state)
    {
        await SeedAsync();

        var thrown = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(change));

        Assert.Equal(state, thrown.SqlState);
    }

    [Fact]
    public async Task ABriefCitationKeepsItsStatementRevision()
    {
        await SeedAsync();
        await ExecuteAsync("""
            DELETE FROM statement_review_decisions;
            DELETE FROM statement_revision_speakers;
            DELETE FROM statement_revision_other_speakers;
            DELETE FROM statement_revision_issues;
            DELETE FROM statement_revision_policy_areas;
            DELETE FROM statement_revision_quotations;
            DELETE FROM statement_revision_bill_numbers
            """);

        var thrown = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync("DELETE FROM statement_revisions"));

        Assert.Equal(PostgresErrorCodes.RestrictViolation, thrown.SqlState);
        Assert.Equal("FK_brief_revision_citations_statement_revisions_statement_cand~", thrown.ConstraintName);
    }

    private async Task SeedAsync(string reviewStatus = "AwaitingReview")
    {
        await seeder.ReleaseCitedRecordsAsync();
        await seeder.ReviewableBriefAsync(WithSuggestion, reviewStatus);
        await ExecuteAsync($"""
            INSERT INTO brief_analysis_runs (run_id, record_id, task, task_version, model, prompt_version, schema_version, input_hash, outcome,
                input_tokens, output_tokens, cache_read_tokens, cache_write_tokens, charged_tokens, started_at_utc_ticks, finished_at_utc_ticks)
            VALUES ('{SuggestionRunId}', '{WithSuggestion.RecordId}', 'brief-draft', 'v1', 'model', 'p1', 's1', repeat('0', 64), 'Drafted', 0, 0, 0, 0, 0, 1, 1)
            """);
    }

    private Task<RevisionBackfillResult> BackfillAsync(int limit) => new BackfillRevisions(BackfillStore()).ExecuteAsync(Owner, limit);

    private Task<RevisionVerification> VerifyAsync() => new VerifyRevisions(BackfillStore()).ExecuteAsync(Owner);

    private PostgresBriefRevisionBackfillStore BackfillStore() => new(ContextFactory());

    private PostgresBriefReviewStore Store() => new(ContextFactory());

    private PostgresReviewQueueStore QueueStore() => new(ContextFactory());

    private IDbContextFactory<CollectionAttemptDbContext> ContextFactory() => new PooledDbContextFactory<CollectionAttemptDbContext>(
        new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options);

    private async Task<List<string>> SnapshotAsync()
    {
        var snapshot = new List<string>();
        foreach (var table in new[] { "brief_revisions", "brief_revision_citations", "brief_revision_paragraphs", "brief_revision_paragraph_citations" })
            snapshot.Add((string)(await ScalarAsync($"SELECT coalesce(string_agg(to_jsonb(t)::text, '|' ORDER BY to_jsonb(t)::text), '') FROM {table} t"))!);
        return snapshot;
    }

    private async Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<object?> ScalarAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        var value = await command.ExecuteScalarAsync();
        return value is DBNull ? null : value;
    }

    private sealed class QueryCounter : DbCommandInterceptor
    {
        public int Count { get; set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Count++;
            return ValueTask.FromResult(result);
        }
    }
}
