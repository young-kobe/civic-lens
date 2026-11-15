using CivicLens.Infrastructure.Collection;
using CivicLens.Tests.Infrastructure.Collection;
using CivicLens.Tests.PublicApi;
using CivicLens.Tests.PublicApi.Bills;
using CivicLens.Tests.PublicApi.Votes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using static CivicLens.Tests.PublicApi.Votes.ViewsSeed;

namespace CivicLens.Tests.Infrastructure.PublicApi;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PublicApiHideCutBillLinesMigrationTests(PostgresCollection postgres)
{
    private const string Migration = "20261115000000_HideCutBillLines";
    private const string WholeLine = "This bill funds rent aid.";
    private const string ReadBills = "SELECT right(record_id, 1) || ':' || coalesce(bill_line, '-') FROM public_api.bills ORDER BY record_id";
    private const string ReadEntries = "SELECT right(record_id, 1) || ':' || coalesce(bill_line, '-') FROM public_api.record_entries ORDER BY record_id";

    [Fact]
    public async Task TheBillAndEntryViewsShowNoLineThatEndsWithTheCutMarkAndKeepAWholeLineWhileTheStoredRowKeepsBoth()
    {
        var database = await postgres.CreateMigratedDatabaseAsync();
        try
        {
            await SeedAsync(database);

            string[] expected = [$"a:{WholeLine}", "b:-"];
            Assert.Equal(expected, await QueryAsPublicAsync(database, ReadBills));
            Assert.Equal(expected, await QueryAsPublicAsync(database, ReadEntries));
            Assert.Equal([$"a:{WholeLine}", $"b:{PublishedSamples.CutBillLine}"], await QueryAsync(database,
                "SELECT right(record_id, 1) || ':' || bill_line FROM published_entries ORDER BY record_id"));
        }
        finally { await postgres.DropDatabaseAsync(database); }
    }

    [Fact]
    public async Task DowngradeShowsTheStoredLineAgainAndUpgradeHidesTheCutLineAgain()
    {
        var database = await postgres.CreateMigratedDatabaseAsync();
        try
        {
            await SeedAsync(database);
            await using var db = new CollectionAttemptDbContext(new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(database).Options);
            var previous = db.Database.GetMigrations().Last(id => string.CompareOrdinal(id, Migration) < 0);
            await db.GetService<IMigrator>().MigrateAsync(previous);

            string[] stored = [$"a:{WholeLine}", $"b:{PublishedSamples.CutBillLine}"];
            Assert.Equal(stored, await QueryAsPublicAsync(database, ReadBills));
            Assert.Equal(stored, await QueryAsPublicAsync(database, ReadEntries));

            await db.GetService<IMigrator>().MigrateAsync();
            Assert.Equal([$"a:{WholeLine}", "b:-"], await QueryAsPublicAsync(database, ReadBills));
        }
        finally { await postgres.DropDatabaseAsync(database); }
    }

    private static async Task SeedAsync(string database)
    {
        await ActivateReleaseAsync(database, 1, DateTimeOffset.UnixEpoch);
        await BillsSeed.AddBillsAsync(database,
            new SeededBill('a', "2026-03-02") { BillLine = WholeLine },
            new SeededBill('b', "2026-03-02") { BillLine = PublishedSamples.CutBillLine });
    }
}
