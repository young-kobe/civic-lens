using CivicLens.Infrastructure.Collection;
using CivicLens.Tests.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.Actions;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class ActionRevisionIntroducedOnMigrationTests(PostgresCollection postgres) : IAsyncLifetime
{
    private const string PreviousMigration = "20261114230000_VoteMemberListEntriesCoveringIndex";
    private const string ViewDefinitions = """
        SELECT c.relname || ' ' || coalesce(array_to_string(c.reloptions, ','), '') || ' ' || pg_get_userbyid(c.relowner) || ' ' || pg_get_viewdef(c.oid)
          FROM pg_class c WHERE c.relnamespace = 'public_api'::regnamespace AND c.relkind = 'v' ORDER BY c.relname
        """;

    private string connectionString = null!;

    public async Task InitializeAsync() => connectionString = await postgres.CreateMigratedDatabaseAsync();

    public Task DisposeAsync() => postgres.DropDatabaseAsync(connectionString);

    [Fact]
    public async Task UpgradeGuardsEveryBillViewAndAddsTheColumnAndRollbackRestoresEveryPublicViewExactly()
    {
        await MigrateToAsync(PreviousMigration);
        var before = await ReadAsync(ViewDefinitions);
        Assert.Empty(await ReadAsync("SELECT column_name FROM information_schema.columns WHERE table_name = 'action_revisions' AND column_name = 'introduced_on'"));

        await PostgresCollectionAttemptStore.FromConnectionString(connectionString).MigrateAsync();

        Assert.Equal(["introduced_on date"], await ReadAsync(
            "SELECT column_name || ' ' || data_type FROM information_schema.columns WHERE table_schema = 'public_api' AND table_name = 'bills' AND ordinal_position = (SELECT max(ordinal_position) FROM information_schema.columns WHERE table_schema = 'public_api' AND table_name = 'bills')"));
        Assert.Equal(8, (await ReadAsync("SELECT viewname FROM pg_views WHERE schemaname = 'public_api' AND definition LIKE '%r.event_on IS NOT NULL%' AND definition LIKE '%r.introduced_on IS NOT NULL%'")).Length);
        Assert.Equal(["true"], await ReadAsync("""
            SET ROLE civic_lens_public;
            SELECT (count(introduced_on) = 0)::text FROM public_api.bills;
            """));

        await MigrateToAsync(PreviousMigration);

        Assert.Equal(before, await ReadAsync(ViewDefinitions));
        Assert.Empty(await ReadAsync("SELECT column_name FROM information_schema.columns WHERE table_name = 'action_revisions' AND column_name = 'introduced_on'"));
        Assert.Equal(["0"], await ReadAsync("""
            SET ROLE civic_lens_public;
            SELECT (SELECT count(*) FROM public_api.bills) + (SELECT count(*) FROM public_api.bill_actions) + (SELECT count(*) FROM public_api.votes)
                 + (SELECT count(*) FROM public_api.action_sources);
            """));
    }

    private async Task MigrateToAsync(string migration)
    {
        await using var db = new CollectionAttemptDbContext(new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options);
        await db.GetService<IMigrator>().MigrateAsync(migration);
    }

    private async Task<string[]> ReadAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<string>();
        do
        {
            while (await reader.ReadAsync()) rows.Add(reader.GetValue(0).ToString()!);
        }
        while (await reader.NextResultAsync());
        return [.. rows];
    }
}
