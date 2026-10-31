using CivicLens.Infrastructure.Collection;
using CivicLens.Tests.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.Review;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class ReviewHeldReasonMigrationTests(PostgresCollection postgres) : IAsyncLifetime
{
    private const string Migration = "20261031001908_ReviewHeldReason";
    private static readonly string[] DraftTables =
        ["action_drafts", "vote_drafts", "statement_drafts", "document_change_drafts", "executive_action_drafts", "brief_drafts", "relationship_drafts"];

    private string connectionString = null!;

    public async Task InitializeAsync()
    {
        connectionString = await postgres.CreateEmptyDatabaseAsync();
        await using var db = NewContext();
        await db.GetService<IMigrator>().MigrateAsync(db.Database.GetMigrations().TakeWhile(id => id != Migration).Last());
    }

    public Task DisposeAsync() => postgres.DropDatabaseAsync(connectionString);

    // A statement without a revision has nothing to take the time from, so it takes its creation time.
    [Fact]
    public async Task TheBackfillTakesTheNewestRevisionTimeAndTheCreationTimeOfAStatementWithoutARevision()
    {
        await ExecuteAsync("""
            SET session_replication_role = replica;
            INSERT INTO statement_drafts (candidate_id, current_revision_number, review_state_version, review_status, created_at_utc_ticks)
            VALUES ('s-revised', 2, 0, 'AwaitingReview', 100), ('s-empty', 0, 0, 'NotDrafted', 300);
            INSERT INTO statement_revisions (candidate_id, revision_number, author_subject, author_kind, revision_json, created_at_utc_ticks)
            VALUES ('s-revised', 1, 'a', 'Person', '{}', 150), ('s-revised', 2, 'a', 'Person', '{}', 250);
            INSERT INTO document_change_drafts (draft_id, comparison_id, current_revision_number, review_state_version, review_status, created_at_utc_ticks)
            VALUES (repeat('a', 32), 'c', 2, 0, 'AwaitingReview', 100);
            INSERT INTO document_change_revisions (draft_id, revision_number, revision_json, created_at_utc_ticks)
            VALUES (repeat('a', 32), 1, '{}', 150), (repeat('a', 32), 2, '{}', 400)
            """);

        await MigrateForwardAsync();

        Assert.Equal(["s-empty|300", "s-revised|250"], await ReadAsync("SELECT candidate_id || '|' || last_revised_at_utc_ticks FROM statement_drafts ORDER BY 1"));
        Assert.Equal(["400"], await ReadAsync("SELECT last_revised_at_utc_ticks::text FROM document_change_drafts"));
    }

    [Fact]
    public async Task EveryDraftTableGetsAPartialNeedsActionIndexAndTheFourCodeApprovedKindsGetAHeldReason()
    {
        await MigrateForwardAsync();

        Assert.Equal(DraftTables.Select(table => $"ix_{table}_needs_action_revised").Order(),
            await ReadAsync("SELECT indexname FROM pg_indexes WHERE indexname LIKE 'ix\\_%\\_needs\\_action\\_revised' ORDER BY 1"));
        Assert.Equal(["action_drafts", "executive_action_drafts", "statement_drafts", "vote_drafts"],
            await ReadAsync("SELECT table_name FROM information_schema.columns WHERE column_name = 'held_reason' ORDER BY 1"));
        Assert.All(await ReadAsync("SELECT indexdef FROM pg_indexes WHERE indexname LIKE 'ix\\_%\\_needs\\_action\\_revised'"),
            definition => Assert.Contains("review_status)::text <> 'Approved'", definition));
    }

    [Fact]
    public async Task TheMigrationGoesDownToTheEarlierSchemaBecauseDownMustWorkForARollback()
    {
        await MigrateForwardAsync();

        await MigrateBackAsync();

        Assert.Empty(await ReadAsync("SELECT 1 FROM information_schema.columns WHERE column_name = 'held_reason'"));
        Assert.Empty(await ReadAsync("SELECT 1 FROM pg_indexes WHERE indexname LIKE 'ix\\_%\\_needs\\_action\\_revised'"));
        Assert.Equal(["action_drafts", "brief_drafts", "executive_action_drafts", "relationship_drafts", "vote_drafts"],
            await ReadAsync("SELECT table_name FROM information_schema.columns WHERE column_name = 'last_revised_at_utc_ticks' ORDER BY 1"));
    }

    private CollectionAttemptDbContext NewContext() =>
        new(new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options);

    private async Task MigrateForwardAsync()
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync();
    }

    private async Task MigrateBackAsync()
    {
        await using var db = NewContext();
        await db.GetService<IMigrator>().MigrateAsync(db.Database.GetMigrations().TakeWhile(id => id != Migration).Last());
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
