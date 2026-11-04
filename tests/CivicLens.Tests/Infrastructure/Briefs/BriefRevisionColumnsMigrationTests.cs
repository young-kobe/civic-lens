using System.Text.Json;
using CivicLens.Application.Review;
using CivicLens.Core.Briefs;
using CivicLens.Core.Review;
using CivicLens.Infrastructure.Briefs;
using CivicLens.Infrastructure.Collection;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.Briefs;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class BriefRevisionColumnsMigrationTests(PostgresCollection postgres) : IAsyncLifetime
{
    private const string Migration = "20261104013231_BriefRevisionColumns";

    private string connectionString = null!;

    public async Task InitializeAsync()
    {
        connectionString = await postgres.CreateEmptyDatabaseAsync();
        await MigrateToAsync(Before());
    }

    public Task DisposeAsync() => postgres.DropDatabaseAsync(connectionString);

    [Fact]
    public async Task ARevisionOfTheOldShapeKeepsItsJsonAndHasNullColumnsAfterTheMigrationBecauseOnlyTheBackfillMovesData()
    {
        var original = await InsertOldShapeAsync();

        await MigrateToAsync(Migration);

        Assert.Equal([JsonSerializer.Serialize(original, JsonSerializerOptions.Web)], await ReadAsync("SELECT revision_json FROM brief_revisions"));
        Assert.Equal(["<null>"], await ReadAsync("SELECT coalesce(headline, '<null>') FROM brief_revisions"));
    }

    [Fact]
    public async Task ARevisionOfTheOldShapeMovesToColumnsAfterTheMigrationAndVerifiesWithNoMismatchBecauseTheColumnsRebuildTheOriginalRevision()
    {
        var original = await InsertOldShapeAsync();
        await MigrateToAsync(Migration);
        await using (var db = NewContext()) await db.Database.MigrateAsync();
        var store = new PostgresBriefRevisionBackfillStore(new PooledDbContextFactory<CollectionAttemptDbContext>(
            new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options));
        var owner = new ReviewActor("auth0|owner", ReviewRole.Owner);

        var moved = await new BackfillRevisions(store).ExecuteAsync(owner, 10);
        var verification = await new VerifyRevisions(store).ExecuteAsync(owner);

        Assert.Equal((1, 1, 0), (moved.Examined, moved.Moved, moved.Skipped));
        Assert.Equal((1, 0, 0), (verification.Checked, verification.Legacy, verification.Mismatched));
        Assert.Equal([JsonSerializer.Serialize(original, JsonSerializerOptions.Web)], await ReadAsync("SELECT revision_json FROM brief_revisions"));
    }

    [Fact]
    public async Task TheRollbackAndANewRunKeepTheJsonBecauseTheJsonIsTheOnlyCopyUntilTheBackfill()
    {
        await InsertOldShapeAsync();
        await MigrateToAsync(Migration);

        await MigrateToAsync(Before());
        await MigrateToAsync(Migration);

        Assert.Single(await ReadAsync("SELECT revision_json FROM brief_revisions"));
        Assert.Empty(await ReadAsync("SELECT 1 FROM brief_revision_citations"));
    }

    private async Task<BriefRevision> InsertOldShapeAsync()
    {
        var revision = BriefFixtures.Revision(author: "auth0|editor") with { TextOrigin = ReviewTextOrigin.Person };
        var json = JsonSerializer.Serialize(revision, JsonSerializerOptions.Web);
        await ExecuteAsync($"""
            SET session_replication_role = replica;
            INSERT INTO brief_drafts (record_id, week_start, current_revision_number, review_state_version, review_status, created_at_utc_ticks, last_revised_at_utc_ticks)
            VALUES ('{revision.RecordId}', '{revision.Window.WeekStart:yyyy-MM-dd}', 1, 0, 'AwaitingReview', 1, 1);
            INSERT INTO brief_revisions (record_id, revision_number, author_subject, author_kind, run_id, revision_json, created_at_utc_ticks)
            VALUES ('{revision.RecordId}', 1, 'auth0|editor', 'Person', NULL, '{json}', {revision.CreatedAtUtc.UtcTicks});
            INSERT INTO statement_revisions (candidate_id, revision_number, author_subject, author_kind, run_id, revision_json, created_at_utc_ticks)
            VALUES ('{new string('1', 64)}', 1, 'auth0|editor', 'Person', NULL, 'x', 1);
            INSERT INTO document_change_revisions (draft_id, revision_number, revision_json, created_at_utc_ticks)
            VALUES ('{new string('2', 32)}', 3, 'x', 1)
            """);
        return revision;
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
