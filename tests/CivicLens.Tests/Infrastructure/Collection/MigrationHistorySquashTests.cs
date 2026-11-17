using CivicLens.Infrastructure.Collection;
using Npgsql;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CivicLens.Tests.Infrastructure.Collection;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class MigrationHistorySquashTests(PostgresCollection postgres) : IAsyncLifetime
{
    private const string Newest = "20261116171634_ReleaseOfficialRecordCounts";
    private const string Current = "20261117143000_StatementLinkedVoteCast";
    private string? connectionString;

    public async Task InitializeAsync() => connectionString = await postgres.CreateMigratedDatabaseAsync();

    public Task DisposeAsync() => postgres.DropDatabaseAsync(connectionString);

    [Fact]
    public async Task TheExactPreSquashHistoryMovesToTheBaselineAndTheTablesAndRowsStay()
    {
        await WriteHistoryAsync(MigrationHistorySquash.PreSquashIds);
        await ExecuteAsync($"""
            INSERT INTO publication_releases (release_number, directory_name, actor_subject, published_at_utc_ticks, record_count, is_active)
            VALUES (1, '000001-{new string('a', 32)}', 'owner', 0, 0, true)
            """);

        await Store().MigrateAsync();

        Assert.Equal(Newest, MigrationHistorySquash.PreSquashIds[^1]);
        Assert.Equal([MigrationHistorySquash.BaselineId, Current], await ReadHistoryAsync());
        Assert.Equal(["1"], await QueryAsync("SELECT release_number::text FROM public_api.active_release"));
    }

    [Fact]
    public async Task TheMoveDeletesAndInsertsInOneTransactionSoAFailedInsertKeepsTheOldHistory()
    {
        await WriteHistoryAsync(MigrationHistorySquash.PreSquashIds);
        await ExecuteAsync("""
            CREATE FUNCTION refuse_history_insert() RETURNS trigger AS $$ BEGIN RAISE EXCEPTION 'refused'; END; $$ LANGUAGE plpgsql;
            CREATE TRIGGER refuse_history_insert BEFORE INSERT ON "__EFMigrationsHistory" FOR EACH ROW EXECUTE FUNCTION refuse_history_insert();
            """);

        await Assert.ThrowsAsync<PostgresException>(() => Store().MigrateAsync());

        Assert.Equal(MigrationHistorySquash.PreSquashIds, await ReadHistoryAsync());
    }

    [Fact]
    public async Task AnEmptyDatabaseRunsTheBaseline()
    {
        var empty = await postgres.CreateEmptyDatabaseAsync();
        try
        {
            await PostgresCollectionAttemptStore.FromConnectionString(empty).MigrateAsync();

            Assert.Equal([MigrationHistorySquash.BaselineId, Current], await ReadHistoryAsync(empty));
            Assert.Equal(["76"], await QueryAsync("SELECT count(*)::text FROM pg_views WHERE schemaname = 'public_api'", empty));
        }
        finally
        {
            await postgres.DropDatabaseAsync(empty);
        }
    }

    [Fact]
    public async Task AMissingDatabaseIsCreatedAndRunsTheBaselineBecauseTheStepNeverOpensADatabaseThatDoesNotExist()
    {
        var empty = await postgres.CreateEmptyDatabaseAsync();
        var missing = new NpgsqlConnectionStringBuilder(empty) { Database = "test_" + Guid.NewGuid().ToString("N") }.ConnectionString;
        try
        {
            await PostgresCollectionAttemptStore.FromConnectionString(missing).MigrateAsync();

            Assert.Equal([MigrationHistorySquash.BaselineId, Current], await ReadHistoryAsync(missing));
        }
        finally
        {
            await postgres.DropDatabaseAsync(missing);
            await postgres.DropDatabaseAsync(empty);
        }
    }

    [Fact]
    public async Task AnEmptyHistoryTableRunsTheBaseline()
    {
        var empty = await postgres.CreateEmptyDatabaseAsync();
        try
        {
            await ExecuteAsync("""
                CREATE TABLE "__EFMigrationsHistory" ("MigrationId" character varying(150) PRIMARY KEY, "ProductVersion" character varying(32) NOT NULL)
                """, empty);

            await PostgresCollectionAttemptStore.FromConnectionString(empty).MigrateAsync();

            Assert.Equal([MigrationHistorySquash.BaselineId, Current], await ReadHistoryAsync(empty));
        }
        finally
        {
            await postgres.DropDatabaseAsync(empty);
        }
    }

    public static TheoryData<string> UnknownHistories => new()
    {
        "missing newest",
        "one more",
        "one unknown",
        "one replaced",
    };

    [Theory]
    [MemberData(nameof(UnknownHistories))]
    public async Task APartialOrUnknownHistoryFailsWithItsIdsAndLeavesTheHistoryUnchanged(string history)
    {
        string[] ids = history switch
        {
            "missing newest" => [.. MigrationHistorySquash.PreSquashIds.SkipLast(1)],
            "one more" => [.. MigrationHistorySquash.PreSquashIds, "20261117000000_Later"],
            "one unknown" => ["20261026010648_Unknown"],
            _ => [.. MigrationHistorySquash.PreSquashIds.Take(60), "20261105000000_Unknown", .. MigrationHistorySquash.PreSquashIds.Skip(61)],
        };
        await WriteHistoryAsync(ids);
        var before = await QueryAsync("""SELECT "MigrationId" || '|' || "ProductVersion" FROM "__EFMigrationsHistory" ORDER BY 1""");

        var failure = await Assert.ThrowsAsync<UnknownMigrationHistoryException>(() => Store().MigrateAsync());

        Assert.Contains($"holds {ids.Length} migrations, and the newest is {ids.Max(StringComparer.Ordinal)}", failure.Message, StringComparison.Ordinal);
        Assert.Contains(Newest, failure.Message, StringComparison.Ordinal);
        Assert.Equal(before, await QueryAsync("""SELECT "MigrationId" || '|' || "ProductVersion" FROM "__EFMigrationsHistory" ORDER BY 1"""));
    }

    [Fact]
    public async Task ASecondMigrateAfterTheMoveChangesNothing()
    {
        await WriteHistoryAsync(MigrationHistorySquash.PreSquashIds);
        await Store().MigrateAsync();
        var moved = await QueryAsync("""SELECT "MigrationId" || '|' || "ProductVersion" FROM "__EFMigrationsHistory" ORDER BY 1""");

        await Store().MigrateAsync();

        Assert.Equal(moved, await QueryAsync("""SELECT "MigrationId" || '|' || "ProductVersion" FROM "__EFMigrationsHistory" ORDER BY 1"""));
    }

    [Fact]
    public async Task TheLinkedVoteViewMigrationCanRollBackAndUpgradeWithoutChangingTheModelOrPublicIsolation()
    {
        await using var db = new CollectionAttemptDbContext(new DbContextOptionsBuilder<CollectionAttemptDbContext>()
            .UseNpgsql(connectionString).Options);
        Assert.Contains(Current, db.Database.GetMigrations());
        Assert.False(db.Database.HasPendingModelChanges());
        await AssertRelationshipViewOwnershipAsync();

        await db.GetService<IMigrator>().MigrateAsync(MigrationHistorySquash.BaselineId);

        Assert.Empty(await LinkedVoteColumnsAsync());
        await AssertRelationshipViewOwnershipAsync();
        await ExecuteAsync("SET ROLE civic_lens_public; SELECT record_id FROM public_api.relationships LIMIT 0");

        await Store().MigrateAsync();

        Assert.Equal(["cited_vote_cast", "cited_vote_date", "linked_official_id"], await LinkedVoteColumnsAsync());
        await AssertRelationshipViewOwnershipAsync();
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.False(db.Database.HasPendingModelChanges());
        await ExecuteAsync("SET ROLE civic_lens_public; SELECT linked_official_id, cited_vote_cast, cited_vote_date FROM public_api.relationships LIMIT 0");
        var denied = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            "SET ROLE civic_lens_public; SELECT official_id FROM public.relationship_revisions LIMIT 0"));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, denied.SqlState);
    }

    private async Task AssertRelationshipViewOwnershipAsync() => Assert.Equal(["civic_lens_public_owner|true"], await QueryAsync("""
        SELECT pg_get_userbyid(relowner) || '|' || coalesce(reloptions @> ARRAY['security_barrier=true'], false)::text
          FROM pg_class WHERE oid = 'public_api.relationships'::regclass
        """));

    private Task<string[]> LinkedVoteColumnsAsync() => QueryAsync("""
        SELECT column_name FROM information_schema.columns
         WHERE table_schema = 'public_api' AND table_name = 'relationships'
           AND column_name IN ('linked_official_id', 'cited_vote_cast', 'cited_vote_date') ORDER BY column_name
        """);

    private PostgresCollectionAttemptStore Store() => PostgresCollectionAttemptStore.FromConnectionString(connectionString!);

    // The migrated database holds the baseline schema. Its history is replaced, so it looks like production at the newest pre-squash migration.
    private Task WriteHistoryAsync(IEnumerable<string> ids) => ExecuteAsync(
        $"""DELETE FROM "__EFMigrationsHistory"; INSERT INTO "__EFMigrationsHistory" SELECT id, '10.0.14' FROM unnest(ARRAY[{string.Join(", ", ids.Select(id => $"'{id}'"))}]::text[]) AS id;""");

    private Task<string[]> ReadHistoryAsync(string? database = null) =>
        QueryAsync("""SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY 1""", database);

    private async Task ExecuteAsync(string sql, string? database = null)
    {
        await using var connection = new NpgsqlConnection(database ?? connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<string[]> QueryAsync(string sql, string? database = null)
    {
        await using var connection = new NpgsqlConnection(database ?? connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<string>();
        while (await reader.ReadAsync())
            rows.Add(reader.GetString(0));
        return [.. rows];
    }
}
