using CivicLens.Infrastructure.Collection;
using CivicLens.Tests.Infrastructure.Collection;
using CivicLens.Tests.PublicApi.Votes;
using CivicLens.Tests.PublicApi.Bills;
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

    // One row from each of the four views that read the bill view: the bill itself, its keys, its sources, and its sponsorship.
    private const string PublicBillReads = """
        SET ROLE civic_lens_public;
        SELECT (SELECT count(*) FROM public_api.bills) + (SELECT count(*) FROM public_api.bill_keys) + (SELECT count(*) FROM public_api.action_sources)
             + (SELECT count(*) FROM public_api.bill_sponsorships);
        """;

    private string connectionString = null!;

    public async Task InitializeAsync() => connectionString = await postgres.CreateMigratedDatabaseAsync();

    public Task DisposeAsync() => postgres.DropDatabaseAsync(connectionString);

    /// <summary>A bill seeded before the column stays in every public bill view after the upgrade, with a null date, and after the rollback.</summary>
    [Fact]
    public async Task UpgradeAddsANullableColumnThatHidesNoBillAndRollbackRestoresEveryPublicViewExactly()
    {
        await MigrateToAsync(PreviousMigration);
        await ViewsSeed.ActivateReleaseAsync(connectionString, 1, new DateTimeOffset(2026, 6, 2, 0, 0, 0, TimeSpan.Zero));
        await BillsSeed.AddBillsBeforeIntroducedOnAsync(connectionString, new SeededBill('b', "2026-01-01") { Sponsorships = [new("p1", "Sponsor", "2026-01-01")] });
        var before = await ReadAsync(ViewDefinitions);
        Assert.Empty(await ReadAsync("SELECT column_name FROM information_schema.columns WHERE table_name = 'action_revisions' AND column_name = 'introduced_on'"));

        await PostgresCollectionAttemptStore.FromConnectionString(connectionString).MigrateAsync();

        Assert.Equal(["introduced_on date"], await ReadAsync(
            "SELECT column_name || ' ' || data_type FROM information_schema.columns WHERE table_schema = 'public_api' AND table_name = 'bills' AND ordinal_position = (SELECT max(ordinal_position) FROM information_schema.columns WHERE table_schema = 'public_api' AND table_name = 'bills')"));
        Assert.Empty(await ReadAsync("SELECT viewname FROM pg_views WHERE schemaname = 'public_api' AND definition LIKE '%introduced_on IS NOT NULL%'"));
        Assert.Equal(["1 0"], await ReadAsync("""
            SET ROLE civic_lens_public;
            SELECT count(*) || ' ' || count(introduced_on) FROM public_api.bills;
            """));
        Assert.Equal(["4"], await ReadAsync(PublicBillReads));

        await MigrateToAsync(PreviousMigration);

        Assert.Equal(before, await ReadAsync(ViewDefinitions));
        Assert.Empty(await ReadAsync("SELECT column_name FROM information_schema.columns WHERE table_name = 'action_revisions' AND column_name = 'introduced_on'"));
        Assert.Equal(["4"], await ReadAsync(PublicBillReads));
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
