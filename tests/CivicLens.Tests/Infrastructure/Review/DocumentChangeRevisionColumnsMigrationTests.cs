using System.Collections.Immutable;
using System.Text.Json;
using CivicLens.Application.Review;
using CivicLens.Core.Documents;
using CivicLens.Core.Review;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Review;
using CivicLens.Tests.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.Review;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class DocumentChangeRevisionColumnsMigrationTests(PostgresCollection postgres) : IAsyncLifetime
{
    private const string Migration = "20261104010500_DocumentChangeRevisionColumns";
    private static readonly string DraftId = new('a', 32);
    private static readonly string ComparisonId = new('c', 64);
    private static readonly string ExtractionId = new('e', 64);

    private string connectionString = null!;

    public async Task InitializeAsync()
    {
        connectionString = await postgres.CreateEmptyDatabaseAsync();
        await MigrateToAsync(Before());
    }

    public Task DisposeAsync() => postgres.DropDatabaseAsync(connectionString);

    [Fact]
    public async Task TheAuthorOfEachRevisionMovesFromTheJsonToTheAuthorColumnsBecauseNoOtherCopyOfItExists()
    {
        await InsertDraftAsync();
        await InsertRevisionAsync(1, """{"authorSubject":"auth0|person"}""");
        await InsertRevisionAsync(2, """{"authorSubject":"civic-lens:analysis"}""");
        await InsertRevisionAsync(3, """{"authorSubject":"civic-lens:code"}""");

        await MigrateToAsync(Migration);

        Assert.Equal(["1|Person|auth0|person|<null>", "2|Analysis|civic-lens:analysis|<null>", "3|Code|civic-lens:code|<null>"],
            await ReadAsync("SELECT revision_number || '|' || author_kind || '|' || author_subject || '|' || coalesce(headline, '<null>') FROM document_change_revisions ORDER BY 1"));
    }

    [Theory]
    [InlineData("not json", "is not valid JSON")]
    [InlineData("{}", "has no valid author")]
    [InlineData("""{"authorSubject":"  "}""", "has no valid author")]
    [InlineData("""{"authorSubject":7}""", "has no valid author")]
    public async Task ARevisionWithoutAReadableAuthorFailsTheMigrationAndNamesItsRowBecauseAGenericErrorDoesNotSayWhichRowToFix(string json, string message)
    {
        await InsertDraftAsync();
        await InsertRevisionAsync(2, json);

        var failure = await Assert.ThrowsAsync<PostgresException>(() => MigrateToAsync(Migration));

        Assert.Contains(message, failure.MessageText);
        Assert.Contains(DraftId + " 2", failure.MessageText);
    }

    [Fact]
    public async Task ARevisionOfTheOldShapeMovesToColumnsAfterTheMigrationAndVerifiesWithNoMismatchBecauseTheColumnsRebuildTheOriginalRevision()
    {
        var original = OldShapeRevision();
        await InsertDraftAsync();
        await InsertRevisionAsync(2, JsonSerializer.Serialize(original, JsonSerializerOptions.Web), original.CreatedAtUtc.UtcTicks);
        await MigrateToAsync(Migration);
        var store = new PostgresDocumentChangeRevisionBackfillStore(new PooledDbContextFactory<CollectionAttemptDbContext>(
            new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options));
        var owner = new ReviewActor("auth0|owner", ReviewRole.Owner);

        var moved = await new BackfillRevisions(store).ExecuteAsync(owner, 10);
        var verification = await new VerifyRevisions(store).ExecuteAsync(owner);

        Assert.Equal((1, 1, 0), (moved.Examined, moved.Moved, moved.Skipped));
        Assert.Equal((1, 0, 0), (verification.Checked, verification.Legacy, verification.Mismatched));
        Assert.Equal([JsonSerializer.Serialize(original, JsonSerializerOptions.Web)], await ReadAsync("SELECT revision_json FROM document_change_revisions"));
    }

    [Fact]
    public async Task TheRollbackKeepsTheJsonAndTheNextRunFillsTheAuthorAgain()
    {
        await InsertDraftAsync();
        await InsertRevisionAsync(2, """{"authorSubject":"auth0|person"}""");
        await MigrateToAsync(Migration);

        await MigrateToAsync(Before());

        Assert.Empty(await ReadAsync("SELECT 1 FROM information_schema.columns WHERE table_name = 'document_change_revisions' AND column_name = 'author_subject'"));
        Assert.Empty(await ReadAsync("""
            SELECT 1 FROM information_schema.tables
            WHERE table_name IN ('document_change_revision_officials', 'document_change_revision_issues', 'document_change_revision_citations')
            """));
        Assert.Empty(await ReadAsync("SELECT 1 FROM pg_constraint WHERE conname = 'ux_document_change_drafts_draft_comparison'"));
        Assert.Equal(["""{"authorSubject":"auth0|person"}"""], await ReadAsync("SELECT revision_json FROM document_change_revisions"));
        await MigrateToAsync(Migration);
        Assert.Equal(["auth0|person"], await ReadAsync("SELECT author_subject FROM document_change_revisions"));
    }

    private static DocumentChangeDraftRevision OldShapeRevision()
    {
        var citations = ImmutableArray.Create(new SourceSpan(ExtractionId, 0, 5), new SourceSpan(ExtractionId, 10, 5));
        return new(DraftId, 2, ComparisonId, "auth0|person", new DateTimeOffset(2026, 10, 2, 0, 0, 0, TimeSpan.Zero), "Headline", "Summary", null, "Limits",
            "Office", new DateOnly(2026, 10, 1), citations[1], ["official-b", "official-a"], ["issue-a"], citations);
    }

    private Task InsertDraftAsync() => ExecuteAsync($"""
        SET session_replication_role = replica;
        INSERT INTO document_extractions (extraction_id, attempt_id, extraction_version, text, text_sha256)
        VALUES ('{ExtractionId}', 'attempt', 'v1', 'text', repeat('0', 64));
        INSERT INTO document_change_drafts (draft_id, comparison_id, current_revision_number, review_state_version, review_status, created_at_utc_ticks, last_revised_at_utc_ticks)
        VALUES ('{DraftId}', '{ComparisonId}', 2, 0, 'AwaitingReview', 0, 0)
        """);

    private Task InsertRevisionAsync(int number, string json, long createdAtUtcTicks = 0) => ExecuteAsync("""
        SET session_replication_role = replica;
        INSERT INTO document_change_revisions (draft_id, revision_number, revision_json, created_at_utc_ticks) VALUES (@draft, @number, @json, @ticks)
        """, ("draft", DraftId), ("number", number), ("json", json), ("ticks", createdAtUtcTicks));

    private string Before() => NewContext().Database.GetMigrations().TakeWhile(id => id != Migration).Last();

    private CollectionAttemptDbContext NewContext() =>
        new(new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options);

    private async Task MigrateToAsync(string target)
    {
        await using var db = NewContext();
        await db.GetService<IMigrator>().MigrateAsync(target);
    }

    private async Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
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
