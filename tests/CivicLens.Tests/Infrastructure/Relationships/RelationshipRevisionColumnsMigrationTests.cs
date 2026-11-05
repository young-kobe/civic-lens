using System.Text.Json;
using CivicLens.Application.Review;
using CivicLens.Core.Review;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Relationships;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using static CivicLens.Tests.Fixtures.RelationshipFixtures;

namespace CivicLens.Tests.Infrastructure.Relationships;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class RelationshipRevisionColumnsMigrationTests(PostgresCollection postgres) : IAsyncLifetime
{
    private const string Migration = "20261104014100_RelationshipRevisionColumns";

    private string connectionString = null!;
    private RelationshipWorldSeeder seeder = null!;

    public async Task InitializeAsync()
    {
        connectionString = await postgres.CreateEmptyDatabaseAsync();
        await MigrateToAsync(Before());
        seeder = new RelationshipWorldSeeder(connectionString);
    }

    public Task DisposeAsync() => postgres.DropDatabaseAsync(connectionString);

    [Fact]
    public async Task ARevisionOfTheOldShapeKeepsItsRowThroughTheMigrationWithNullColumnsBecauseOnlyTheBackfillMovesData()
    {
        await InsertLegacyRevisionAsync(JsonSerializer.Serialize(Revision(), JsonSerializerOptions.Web));

        await MigrateToAsync(Migration);

        Assert.Equal(["<null>"], await ReadAsync("SELECT coalesce(statement_candidate_id, '<null>') FROM relationship_revisions"));
        Assert.Equal([JsonSerializer.Serialize(Revision(), JsonSerializerOptions.Web)], await ReadAsync("SELECT revision_json FROM relationship_revisions"));
    }

    [Fact]
    public async Task AnUnreadableJsonFailsTheMigrationAndNamesItsRowBecauseAGenericCastErrorDoesNotSayWhichRowToFix()
    {
        await InsertLegacyRevisionAsync("not json");

        var failure = await Assert.ThrowsAsync<PostgresException>(() => MigrateToAsync(Migration));

        Assert.Contains("is not valid JSON", failure.MessageText);
        Assert.Contains(ActionPair().PairId, failure.MessageText);
    }

    [Fact]
    public async Task ARevisionOfTheOldShapeMovesToColumnsAfterTheMigrationAndVerifiesWithNoMismatchBecauseTheColumnsRebuildTheOriginalRevision()
    {
        var json = JsonSerializer.Serialize(Revision(), JsonSerializerOptions.Web);
        await InsertLegacyRevisionAsync(json);
        await MigrateToAsync(Migration);
        await using (var db = NewContext()) await db.Database.MigrateAsync();
        await seeder.ActionAsync(Action());
        await seeder.StatementAsync(Statement());
        var store = new PostgresRelationshipRevisionBackfillStore(new PooledDbContextFactory<CollectionAttemptDbContext>(
            new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options));
        var owner = new ReviewActor("auth0|owner", ReviewRole.Owner);

        var moved = await new BackfillRevisions(store).ExecuteAsync(owner, 10);
        var verification = await new VerifyRevisions(store).ExecuteAsync(owner);

        Assert.Equal((1, 1, 0), (moved.Examined, moved.Moved, moved.Skipped));
        Assert.Equal((1, 0, 0), (verification.Checked, verification.Legacy, verification.Mismatched));
        Assert.Equal([json], await ReadAsync("SELECT revision_json FROM relationship_revisions"));
    }

    [Fact]
    public async Task TheRollbackKeepsTheJsonOfEveryRevisionAndTheNextRunAddsTheColumnsAgain()
    {
        var json = JsonSerializer.Serialize(Revision(), JsonSerializerOptions.Web);
        await InsertLegacyRevisionAsync(json);
        await MigrateToAsync(Migration);

        await MigrateToAsync(Before());

        Assert.Equal([json], await ReadAsync("SELECT revision_json FROM relationship_revisions"));
        await MigrateToAsync(Migration);
        Assert.Single(await ReadAsync("SELECT 1 FROM information_schema.columns WHERE table_name = 'relationship_revisions' AND column_name = 'reason'"));
    }

    private Task InsertLegacyRevisionAsync(string revisionJson) => seeder.ExecuteAsync("""
        SET session_replication_role = replica;
        INSERT INTO relationship_drafts (record_id, current_revision_number, review_state_version, review_status, no_longer_matches,
            created_at_utc_ticks, last_revised_at_utc_ticks)
        VALUES (@id, 1, 0, 'AwaitingReview', false, 1, 1);
        INSERT INTO relationship_revisions (record_id, revision_number, author_subject, author_kind, run_id, revision_json, created_at_utc_ticks)
        VALUES (@id, 1, 'civic-lens:analysis', 'Analysis', substr(md5(@id), 1, 32), @json, @ticks)
        """, ("id", ActionPair().PairId), ("json", revisionJson), ("ticks", Created.UtcTicks));

    private string Before() => NewContext().Database.GetMigrations().TakeWhile(id => id != Migration).Last();

    private CollectionAttemptDbContext NewContext() =>
        new(new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options);

    private async Task MigrateToAsync(string target)
    {
        await using var db = NewContext();
        await db.GetService<IMigrator>().MigrateAsync(target);
    }

    private async Task<List<string>> ReadAsync(string sql) => [.. (await seeder.ReadAsync(sql)).Select(row => row[0])];
}
