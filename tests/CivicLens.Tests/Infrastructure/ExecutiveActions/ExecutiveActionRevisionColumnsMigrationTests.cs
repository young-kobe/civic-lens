using System.Text.Json;
using CivicLens.Application.Review;
using CivicLens.Core.ExecutiveActions;
using CivicLens.Core.Review;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.ExecutiveActions;
using CivicLens.Tests.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.ExecutiveActions;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class ExecutiveActionRevisionColumnsMigrationTests(PostgresCollection postgres) : IAsyncLifetime
{
    private const string Migration = "20261103180809_ExecutiveActionRevisionColumns";
    private static readonly string RecordId = new('a', 64);
    private static readonly string ExtractionId = new('e', 64);
    private static readonly ExecutiveActionNotice Notice = new("R1-2024-26714", new DateOnly(2024, 11, 20), "https://example.test/notice", new(ExtractionId, 12, 34));

    private string connectionString = null!;

    public async Task InitializeAsync()
    {
        connectionString = await postgres.CreateEmptyDatabaseAsync();
        await MigrateToAsync(Before());
    }

    public Task DisposeAsync() => postgres.DropDatabaseAsync(connectionString);

    [Fact]
    public async Task TheNoticeJsonOfADocumentMovesToTheNoticeColumnsAndTheCodeAuthorFillsTheRevisionBecauseTheColumnsAreTheOnlyCopy()
    {
        await InsertRecordAsync(Notice, "civic-lens:code");

        await MigrateToAsync(Migration);

        Assert.Equal(["R1-2024-26714|https://example.test/notice|" + ExtractionId + "|12|34|2024-11-20", "2024-26714|<null>|<null>|<null>|<null>|2024-11-08"],
            await ReadAsync("""
                SELECT document_number || '|' || coalesce(notice_url, '<null>') || '|' || coalesce(notice_citation_extraction_id, '<null>') || '|'
                    || coalesce(notice_citation_start::text, '<null>') || '|' || coalesce(notice_citation_length::text, '<null>') || '|' || published_on
                FROM executive_action_documents ORDER BY notice_url NULLS LAST
                """));
        Assert.Empty(await ReadAsync("SELECT 1 FROM information_schema.columns WHERE column_name = 'notice_json'"));
        Assert.Equal(["Code|civic-lens:code|<null>"], await ReadAsync("SELECT author_kind || '|' || author_subject || '|' || coalesce(title, '<null>') FROM executive_action_revisions"));
    }

    [Theory]
    [InlineData("documentNumber", "\"R1-2024-99999\"")]
    [InlineData("publishedOn", "\"2024-11-21\"")]
    public async Task ANoticeJsonThatDiffersFromItsRowFailsTheMigrationAndKeepsTheJsonBecauseNothingMayBeLostWithoutACopy(string key, string value)
    {
        await InsertRecordAsync(Notice, "civic-lens:code");
        await ExecuteAsync($"UPDATE executive_action_documents SET notice_json = jsonb_set(notice_json::jsonb, '{{{key}}}', '{value}')::text WHERE notice_json IS NOT NULL");

        var failure = await Assert.ThrowsAsync<PostgresException>(() => MigrateToAsync(Migration));

        Assert.Contains("Cannot drop notice_json", failure.MessageText);
        Assert.Contains("R1-2024-26714", failure.MessageText);
        Assert.Single(await ReadAsync("SELECT 1 FROM information_schema.columns WHERE column_name = 'notice_json'"));
    }

    [Theory]
    [InlineData("UPDATE executive_action_documents SET notice_json = 'not json' WHERE notice_json IS NOT NULL", "R1-2024-26714")]
    [InlineData("UPDATE executive_action_revisions SET revision_json = 'not json'", "aaaaaaaa")]
    public async Task AnUnreadableJsonFailsTheMigrationAndNamesItsRowBecauseAGenericCastErrorDoesNotSayWhichRowToFix(string change, string row)
    {
        await InsertRecordAsync(Notice, "civic-lens:code");
        await ExecuteAsync(change);

        var failure = await Assert.ThrowsAsync<PostgresException>(() => MigrateToAsync(Migration));

        Assert.Contains("is not valid JSON", failure.MessageText);
        Assert.Contains(row, failure.MessageText);
    }

    [Fact]
    public async Task ARevisionOfAnotherAuthorFailsTheMigrationBecauseCodeIsTheOnlyAuthorOfThisKind()
    {
        await InsertRecordAsync(Notice, "auth0|person");

        var failure = await Assert.ThrowsAsync<PostgresException>(() => MigrateToAsync(Migration));

        Assert.Contains("Cannot set the code author", failure.MessageText);
        Assert.Contains(RecordId, failure.MessageText);
    }

    [Fact]
    public async Task TheRollbackRebuildsANoticeJsonThatReadsBackAsTheSameNoticeAndTheNextRunKeepsTheColumns()
    {
        await InsertRecordAsync(Notice, "civic-lens:code");
        await MigrateToAsync(Migration);
        var columns = await ReadAsync("SELECT notice_url || notice_citation_start || notice_citation_length FROM executive_action_documents WHERE notice_url IS NOT NULL");

        await MigrateToAsync(Before());

        var json = Assert.Single(await ReadAsync("SELECT notice_json FROM executive_action_documents WHERE notice_json IS NOT NULL"));
        Assert.Equal(Notice, JsonSerializer.Deserialize<ExecutiveActionNotice>(json, JsonSerializerOptions.Web));
        await MigrateToAsync(Migration);
        Assert.Equal(columns, await ReadAsync("SELECT notice_url || notice_citation_start || notice_citation_length FROM executive_action_documents WHERE notice_url IS NOT NULL"));
    }

    [Fact]
    public async Task ARevisionOfTheOldShapeMovesToColumnsAfterTheMigrationAndVerifiesWithNoMismatchBecauseTheColumnsRebuildTheOriginalRevision()
    {
        var original = OldShapeRevision();
        await InsertRecordAsync(Notice, "civic-lens:code", JsonSerializer.Serialize(original, JsonSerializerOptions.Web), original.CreatedAtUtc.UtcTicks);
        await MigrateToAsync(Migration);
        var contexts = new PooledDbContextFactory<CollectionAttemptDbContext>(new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options);
        var owner = new ReviewActor("auth0|owner", ReviewRole.Owner);
        var store = new PostgresExecutiveActionRevisionBackfillStore(contexts);

        var moved = await new BackfillRevisions(store).ExecuteAsync(owner, 10);
        var verification = await new VerifyRevisions(store).ExecuteAsync(owner);

        Assert.Equal((1, 1, 0), (moved.Examined, moved.Moved, moved.Skipped));
        Assert.Equal((1, 0, 0), (verification.Checked, verification.Legacy, verification.Mismatched));
        Assert.Equal([JsonSerializer.Serialize(original, JsonSerializerOptions.Web)], await ReadAsync("SELECT revision_json FROM executive_action_revisions"));
    }

    private static ExecutiveActionRevision OldShapeRevision()
    {
        var facts = ExecutiveActionFacts.Of("2024-26714", ExecutiveActionKind.Proclamation, null, "A Title", new DateOnly(2024, 11, 5),
            new DateOnly(2024, 11, 8), "89 FR 1", "Joe Biden", "https://example.test/document");
        return new(RecordId, 2, "civic-lens:code", new DateTimeOffset(2024, 11, 8, 0, 0, 0, TimeSpan.Zero), "federal-register-documents", "2024-26714",
            ExecutiveActionKind.Proclamation, null, "A Title", new DateOnly(2024, 11, 5), new DateOnly(2024, 11, 8), "89 FR 1", "Joe Biden", "joe-biden",
            "https://example.test/document", [.. facts.Select((fact, position) => new ExecutiveActionFact(fact.Label, fact.Value, new(ExtractionId, position * 10, 5)))],
            [Notice]);
    }

    private Task InsertRecordAsync(ExecutiveActionNotice notice, string authorSubject, string? revisionJson = null, long createdAtUtcTicks = 0)
    {
        var revision = revisionJson ?? JsonSerializer.Serialize(new { recordId = RecordId, revisionNumber = 2, authorSubject }, JsonSerializerOptions.Web);
        var noticeJson = JsonSerializer.Serialize(notice, JsonSerializerOptions.Web);
        return ExecuteAsync($"""
            SET session_replication_role = replica;
            INSERT INTO document_extractions (extraction_id, attempt_id, extraction_version, text, text_sha256)
            VALUES ('{ExtractionId}', 'attempt', 'v1', 'text', repeat('0', 64));
            INSERT INTO executive_action_drafts (record_id, current_revision_number, review_state_version, review_status, created_at_utc_ticks, last_revised_at_utc_ticks)
            VALUES ('{RecordId}', 2, 0, 'AwaitingReview', 0, 0);
            INSERT INTO executive_action_revisions (record_id, revision_number, revision_json, created_at_utc_ticks)
            VALUES ('{RecordId}', 2, '{revision}', {createdAtUtcTicks});
            INSERT INTO executive_action_documents (source_id, document_number, record_id, revision_number, notice_json, published_on, document_job_id)
            VALUES ('federal-register-documents', '2024-26714', '{RecordId}', 2, NULL, '2024-11-08', 'job-1'),
                   ('federal-register-documents', '{notice.DocumentNumber}', '{RecordId}', 2, '{noticeJson}', '{notice.PublishedOn:yyyy-MM-dd}', 'job-2')
            """);
    }

    private string Before() => NewContext().Database.GetMigrations().TakeWhile(id => id != Migration).Last();

    private CollectionAttemptDbContext NewContext() =>
        new(new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options);

    private async Task MigrateToAsync(string target)
    {
        await using var db = NewContext();
        await db.GetService<IMigrator>().MigrateAsync(target);
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<List<string>> ReadAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<string>();
        while (await reader.ReadAsync()) rows.Add(reader.GetValue(0).ToString()!);
        return rows;
    }
}
