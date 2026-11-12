using CivicLens.Infrastructure.Collection;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Infrastructure.Collection;
using CivicLens.Tests.PublicApi.Votes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using static CivicLens.Tests.PublicApi.Votes.ViewsSeed;

namespace CivicLens.Tests.Infrastructure.PublicApi;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PublicApiEntryApprovalTimeMigrationTests(PostgresCollection postgres)
{
    private const string Migration = "20261112060000_PublishedEntryApprovalTime";
    private const string ReadTimes = "SELECT revision_number || '|' || approved_at_utc_ticks FROM public_api.record_entries ORDER BY record_id, revision_number";
    private static readonly DateTimeOffset FirstPublished = new(2026, 3, 2, 12, 0, 0, TimeSpan.Zero);
    private static readonly string ActionId = new('a', 64);
    private static readonly string RetiredId = new('b', 32);
    private static readonly DateTimeOffset OldApproval = FirstPublished.AddHours(1);
    private static readonly DateTimeOffset NewApproval = FirstPublished.AddDays(30).AddMinutes(7);

    [Fact]
    public async Task TheEntryViewShowsTheStoredApprovalTimeOfTheActiveReleaseOnlyAndARollbackMovesIt()
    {
        var database = await postgres.CreateMigratedDatabaseAsync();
        try
        {
            await SeedAsync(database);

            Assert.Equal([$"2|{NewApproval.UtcTicks}"], await QueryAsPublicAsync(database, ReadTimes));

            await ViewsSeed.ActivateReleaseAsync(database, 1, DateTimeOffset.UnixEpoch);
            Assert.Equal([$"1|{OldApproval.UtcTicks}", $"1|{OldApproval.AddMinutes(1).UtcTicks}"], await QueryAsPublicAsync(database, ReadTimes));

            await ExecuteAsync(database, "UPDATE publication_releases SET is_active = false");
            Assert.Empty(await QueryAsPublicAsync(database, ReadTimes));
            var denied = await Assert.ThrowsAsync<PostgresException>(() => QueryAsPublicAsync(database, "SELECT approved_at_utc_ticks FROM public.published_entries"));
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, denied.SqlState);
        }
        finally { await postgres.DropDatabaseAsync(database); }
    }

    [Fact]
    public async Task DowngradeDropsTheColumnAndKeepsTheOlderEntryViewReadableByThePublicRoleAndUpgradeRestoresTheTime()
    {
        var database = await postgres.CreateMigratedDatabaseAsync();
        try
        {
            await SeedAsync(database);
            await using var db = new CollectionAttemptDbContext(new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(database).Options);
            var previous = db.Database.GetMigrations().Last(id => string.CompareOrdinal(id, Migration) < 0);
            await db.GetService<IMigrator>().MigrateAsync(previous);

            Assert.Equal(["0"], await QueryAsync(database, """
                SELECT count(*)::text FROM information_schema.columns
                 WHERE column_name = 'approved_at_utc_ticks' AND table_name IN ('published_entries', 'record_entries')
                """));
            Assert.Equal(["true|true"], await QueryAsync(database, """
                SELECT has_table_privilege('civic_lens_public', 'public_api.record_entries', 'SELECT')::text || '|'
                    || has_column_privilege('civic_lens_public_owner', 'published_entries', 'headline', 'SELECT')::text
                """));
            Assert.Equal([ActionId], await QueryAsPublicAsync(database, "SELECT record_id FROM public_api.record_entries"));

            await db.GetService<IMigrator>().MigrateAsync();
            await ApplyGrantsAsync(database);
            await ExecuteAsync(database, $"UPDATE published_entries SET approved_at_utc_ticks = {NewApproval.UtcTicks} WHERE record_id = '{ActionId}' AND revision_number = 2");
            Assert.Equal([$"2|{NewApproval.UtcTicks}"], await QueryAsPublicAsync(database, ReadTimes));
        }
        finally { await postgres.DropDatabaseAsync(database); }
    }

    [Fact]
    public async Task TheColumnRefusesATickCountOutsideTheDateRangeAndTakesNull()
    {
        var database = await postgres.CreateMigratedDatabaseAsync();
        try
        {
            await SeedAsync(database);

            var refused = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(database, "UPDATE published_entries SET approved_at_utc_ticks = -1"));
            Assert.Equal(PostgresErrorCodes.CheckViolation, refused.SqlState);
            await ExecuteAsync(database, "UPDATE published_entries SET approved_at_utc_ticks = NULL");
        }
        finally { await postgres.DropDatabaseAsync(database); }
    }

    // Release 1 holds revision 1 of the action and a retired document change. Release 2 holds revision 2 of the action only.
    private static async Task SeedAsync(string database)
    {
        PublishedRecordEntry old = PublishedEntryFixtures.Action(ActionId, FirstPublished) with { ApprovedAtUtc = OldApproval };
        var current = PublishedEntryFixtures.Action(ActionId, FirstPublished, revision: 2) with { Headline = "A newer bill headline", ApprovedAtUtc = NewApproval };
        var retired = PublishedEntryFixtures.DocumentChange(RetiredId, 1, "A page changed", FirstPublished.AddDays(-3)) with { ApprovedAtUtc = OldApproval.AddMinutes(1) };
        await PublishedEntryFixtures.SeedEntriesAsync(database, [old, current, retired], allowMissingRevisionParents: true);
        await ViewsSeed.ActivateReleaseAsync(database, 1, DateTimeOffset.UnixEpoch);
        await ViewsSeed.ActivateReleaseAsync(database, 2, DateTimeOffset.UnixEpoch.AddDays(1));
        await ExecuteAsync(database, $"""
            INSERT INTO publication_record_states (kind, record_id, first_release, revision_number, last_release) VALUES
              ('action', '{ActionId}', 1, 1, 1), ('action', '{ActionId}', 2, 2, NULL), ('documentChange', '{RetiredId}', 1, 1, 1);
            """);
    }
}
