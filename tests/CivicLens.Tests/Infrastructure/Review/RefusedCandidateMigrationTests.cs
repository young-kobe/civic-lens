using CivicLens.Infrastructure.Collection;
using CivicLens.Tests.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.Review;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class RefusedCandidateMigrationTests(PostgresCollection postgres) : IAsyncLifetime
{
    private const string Migration = "20261103011201_RefusedCandidate";

    private string connectionString = null!;

    public async Task InitializeAsync() => connectionString = await postgres.CreateMigratedDatabaseAsync();

    public Task DisposeAsync() => postgres.DropDatabaseAsync(connectionString);

    [Theory]
    [InlineData("action")]
    [InlineData("vote")]
    public async Task TheDraftsHoldANullableRefusedCandidateWithAForeignKeyToTheCandidatesBecauseTheBlockMustNameARealUpdate(string prefix)
    {
        Assert.Equal(["YES"], await ReadAsync($"SELECT is_nullable FROM information_schema.columns WHERE table_name = '{prefix}_drafts' AND column_name = 'refused_candidate_id'"));
        Assert.Single(await ReadAsync($"""
            SELECT c.conname FROM pg_constraint c
            WHERE c.conrelid = '{prefix}_drafts'::regclass AND c.contype = 'f' AND c.confrelid = '{prefix}_candidates'::regclass
              AND c.conkey = ARRAY[(SELECT attnum FROM pg_attribute WHERE attrelid = '{prefix}_drafts'::regclass AND attname = 'refused_candidate_id')]
            """));
    }

    [Theory]
    [InlineData("action")]
    [InlineData("vote")]
    public async Task TheMigrationGoesDownAndDropsTheColumnBecauseNoDataNeedsBackfillingBeforeTheFirstDeploy(string prefix)
    {
        await using var db = new CollectionAttemptDbContext(new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options);

        await db.GetService<IMigrator>().MigrateAsync(db.Database.GetMigrations().TakeWhile(id => id != Migration).Last());

        Assert.Empty(await ReadAsync($"SELECT 1 FROM information_schema.columns WHERE table_name = '{prefix}_drafts' AND column_name = 'refused_candidate_id'"));
        Assert.DoesNotContain(Migration, await ReadAsync("""SELECT "MigrationId" FROM "__EFMigrationsHistory" """));
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
