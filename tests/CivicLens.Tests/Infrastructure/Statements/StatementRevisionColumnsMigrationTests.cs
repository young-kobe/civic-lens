using System.Text.Json;
using CivicLens.Application.Review;
using CivicLens.Core.Documents;
using CivicLens.Core.Review;
using CivicLens.Core.Statements;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Statements;
using CivicLens.Tests.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.Statements;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class StatementRevisionColumnsMigrationTests(PostgresCollection postgres) : IAsyncLifetime
{
    private const string Migration = "20261104014500_StatementRevisionColumns";
    private static readonly string CandidateId = new('a', 64);
    private static readonly string ExtractionId = new('e', 64);

    private string connectionString = null!;

    public async Task InitializeAsync()
    {
        connectionString = await postgres.CreateEmptyDatabaseAsync();
        await MigrateToAsync(Before());
    }

    public Task DisposeAsync() => postgres.DropDatabaseAsync(connectionString);

    [Fact]
    public async Task ARevisionOfTheOldShapeMovesToColumnsAfterTheMigrationAndVerifiesWithNoMismatchBecauseTheColumnsRebuildTheOriginalRevision()
    {
        var original = OldShapeRevision();
        await InsertCandidateAsync();
        await InsertRevisionAsync(JsonSerializer.Serialize(original, JsonSerializerOptions.Web), original.CreatedAtUtc.UtcTicks);
        await MigrateToAsync(Migration);
        var store = new PostgresStatementRevisionBackfillStore(new PooledDbContextFactory<CollectionAttemptDbContext>(
            new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options));
        var owner = new ReviewActor("auth0|owner", ReviewRole.Owner);

        var moved = await new BackfillRevisions(store).ExecuteAsync(owner, 10);
        var verification = await new VerifyRevisions(store).ExecuteAsync(owner);

        Assert.Equal((1, 1, 0), (moved.Examined, moved.Moved, moved.Skipped));
        Assert.Equal((1, 0, 0), (verification.Checked, verification.Legacy, verification.Mismatched));
        Assert.Equal([JsonSerializer.Serialize(original, JsonSerializerOptions.Web)], await ReadAsync("SELECT revision_json FROM statement_revisions"));
    }

    [Fact]
    public async Task AnOldRevisionIsALegacyRowAfterTheMigrationBecauseItsColumnsStayNullUntilTheBackfill()
    {
        await InsertCandidateAsync();
        await InsertRevisionAsync("{}", 0);

        await MigrateToAsync(Migration);

        Assert.Equal(["<null>|0"], await ReadAsync("SELECT coalesce(headline, '<null>') || '|' || (SELECT count(*) FROM statement_revision_speakers) FROM statement_revisions"));
    }

    [Fact]
    public async Task TheRollbackKeepsTheJsonAndTheNextRunAddsTheColumnsAgain()
    {
        await InsertCandidateAsync();
        await InsertRevisionAsync("""{"headline":"kept"}""", 0);
        await MigrateToAsync(Migration);

        await MigrateToAsync(Before());

        Assert.Empty(await ReadAsync("SELECT 1 FROM information_schema.columns WHERE table_name = 'statement_revisions' AND column_name = 'headline'"));
        Assert.Empty(await ReadAsync("SELECT 1 FROM information_schema.tables WHERE table_name ~ '^statement_revision_.'"));
        Assert.Empty(await ReadAsync("SELECT 1 FROM pg_constraint WHERE conname = 'ux_statement_candidates_candidate_extraction'"));
        Assert.Equal(["""{"headline":"kept"}"""], await ReadAsync("SELECT revision_json FROM statement_revisions"));
        await MigrateToAsync(Migration);
        Assert.Equal(["<null>"], await ReadAsync("SELECT coalesce(headline, '<null>') FROM statement_revisions"));
    }

    private static StatementDraftRevision OldShapeRevision()
    {
        var quote = new SourceSpan(ExtractionId, 0, 5);
        var evidence = new SourceSpan(ExtractionId, 10, 5);
        return new(CandidateId, 1, "auth0|person", new DateTimeOffset(2026, 10, 2, 0, 0, 0, TimeSpan.Zero), "Headline", "Summary", new DateOnly(2026, 10, 1),
            evidence, ["official-b", "official-a"], ["Jo Rivera"], ["issue-a"], ["area-a"], [new(quote, StatementQuotationKind.DirectQuotation)],
            ["H.R. 56"]);
    }

    private Task InsertCandidateAsync() => ExecuteAsync($"""
        SET session_replication_role = replica;
        INSERT INTO document_extractions (extraction_id, attempt_id, extraction_version, text, text_sha256)
        VALUES ('{ExtractionId}', 'attempt', 'v1', 'text', repeat('0', 64));
        INSERT INTO statement_candidates (candidate_id, source_id, url, extraction_id, attempt_id, created_at_utc_ticks)
        VALUES ('{CandidateId}', 'source', 'https://example.test/a', '{ExtractionId}', 'attempt', 0);
        INSERT INTO statement_drafts (candidate_id, current_revision_number, review_state_version, review_status, created_at_utc_ticks, last_revised_at_utc_ticks)
        VALUES ('{CandidateId}', 1, 0, 'AwaitingReview', 0, 0)
        """);

    private Task InsertRevisionAsync(string json, long createdAtUtcTicks) => ExecuteAsync("""
        SET session_replication_role = replica;
        INSERT INTO statement_revisions (candidate_id, revision_number, author_subject, author_kind, revision_json, created_at_utc_ticks)
        VALUES (@candidate, 1, 'auth0|person', 'Person', @json, @ticks)
        """, ("candidate", CandidateId), ("json", json), ("ticks", createdAtUtcTicks));

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
