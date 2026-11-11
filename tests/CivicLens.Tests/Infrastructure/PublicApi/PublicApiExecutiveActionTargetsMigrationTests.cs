using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Collection.Migrations;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Infrastructure.Collection;
using CivicLens.Tests.PublicApi.Votes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.PublicApi;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PublicApiExecutiveActionTargetsMigrationTests(PostgresCollection postgres) : IAsyncLifetime
{
    private string database = null!;

    public async Task InitializeAsync()
    {
        database = await postgres.CreateMigratedDatabaseAsync();
        await ViewsSeed.ActivateReleaseAsync(database, 1, DateTimeOffset.UnixEpoch);
        await ViewsSeed.ActivateReleaseAsync(database, 2, DateTimeOffset.UnixEpoch.AddDays(1));
        await ViewsSeed.ExecuteAsync(database, "SET session_replication_role = replica;\n" + ExecutiveActionTargetRows.Seed());
    }

    public Task DisposeAsync() => postgres.DropDatabaseAsync(database);

    [Fact]
    public async Task ATargetLinksToTheOneMatchingExecutiveActionOfTheActiveReleaseAndToNoOtherRecord()
    {
        Assert.Equal([
            $"0|Amends|ExecutiveOrder|14100|EO 14100, January 3, 2025|{ExecutiveActionTargetRows.LinkedOrder}",
            "1|Amends|ExecutiveOrder|14101|EO 14101|",
            "2|Amends|ExecutiveOrder|14102|EO 14102|",
            $"3|Revokes|Proclamation|10001|Proc. 10001|{ExecutiveActionTargetRows.LinkedProclamation}",
            "4|Revokes|ExecutiveOrder|14000|EO 14000|"
        ], await QueryAsPublicAsync($"""
            SELECT "position" || '|' || relation || '|' || kind || '|' || number || '|' || reference || '|' || coalesce(target_record_id, '')
              FROM public_api.executive_action_targets WHERE record_id = '{ExecutiveActionTargetRows.Source}' ORDER BY "position"
            """));
    }

    [Fact]
    public async Task ATargetThatMatchesOnlyARecordOfAnOlderReleaseGetsNoRecordBecauseALinkNeverCrossesReleases()
    {
        Assert.Equal([""], await QueryAsPublicAsync($"SELECT coalesce(target_record_id, '') FROM public_api.executive_action_targets WHERE record_id = '{ExecutiveActionTargetRows.Source}' AND number = '14102'"));
    }

    [Fact]
    public async Task ATargetThatTwoActiveRecordsMatchGetsNoRecordBecauseTheViewNeverGuesses()
    {
        Assert.Equal([""], await QueryAsPublicAsync($"SELECT coalesce(target_record_id, '') FROM public_api.executive_action_targets WHERE record_id = '{ExecutiveActionTargetRows.Source}' AND number = '14101'"));
    }

    [Fact]
    public async Task AnExecutiveOrderTargetNeverLinksToAProclamationOfTheSameNumber()
    {
        Assert.Equal([ExecutiveActionTargetRows.LinkedOrder], await QueryAsPublicAsync($"SELECT target_record_id FROM public_api.executive_action_targets WHERE record_id = '{ExecutiveActionTargetRows.Source}' AND number = '14100'"));
    }

    [Fact]
    public async Task ARevisionNeverLinksToItself()
    {
        Assert.Equal([""], await QueryAsPublicAsync($"SELECT coalesce(target_record_id, '') FROM public_api.executive_action_targets WHERE record_id = '{ExecutiveActionTargetRows.Source}' AND number = '14000'"));
    }

    [Fact]
    public async Task AReferenceThatAnAstralCharacterShiftsIsNullBecauseTheViewNeverShowsTextThatIsNotTheReference()
    {
        await ViewsSeed.ExecuteAsync(database, $"UPDATE executive_action_revisions SET disposition_notes = E'\\U0001F600' || disposition_notes WHERE record_id = '{ExecutiveActionTargetRows.Source}'");

        Assert.Equal(["14000|"], await QueryAsPublicAsync($"""
            SELECT number || '|' || coalesce(reference, '') FROM public_api.executive_action_targets WHERE record_id = '{ExecutiveActionTargetRows.Source}' AND number = '14000'
            """));
    }

    [Fact]
    public async Task OnlyActiveRecordsHaveTargetsAndThePublicRoleCannotReadTheTargetTable()
    {
        Assert.Equal(["5"], await QueryAsPublicAsync("SELECT count(*)::text FROM public_api.executive_action_targets"));
        var denied = await Assert.ThrowsAsync<PostgresException>(() => QueryAsPublicAsync("SELECT count(*) FROM public.executive_action_revision_disposition_targets"));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, denied.SqlState);
    }

    [Fact]
    public async Task DowngradeDropsTheViewAndRevokesItsColumnsAndUpgradeWithGrantsRestoresThem()
    {
        var migration = new PublicApiExecutiveActionTargets();
        await ApplyAsync(migration.DownOperations);
        Assert.Equal("false|false|true", await GrantStateAsync());

        await ApplyAsync(migration.UpOperations);
        await using var db = new CollectionAttemptDbContext(new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(database).Options);
        var grants = typeof(PublicApiExecutiveActionTargets).Assembly.GetType("CivicLens.Infrastructure.Publication.PublicApiGrants")!;
        await (Task)grants.GetMethod("ApplyAsync")!.Invoke(null, [db, CancellationToken.None])!;
        Assert.Equal("true|true|false", await GrantStateAsync());
        Assert.Equal(["5"], await QueryAsPublicAsync("SELECT count(*)::text FROM public_api.executive_action_targets"));
    }

    private async Task<string> GrantStateAsync() => (await QueryAsync("""
        SELECT has_column_privilege('civic_lens_public_owner', 'executive_action_revisions', 'disposition_notes', 'SELECT')::text
            || '|' || has_any_column_privilege('civic_lens_public_owner', 'executive_action_revision_disposition_targets', 'SELECT')::text
            || '|' || (to_regclass('public_api.executive_action_targets') IS NULL)::text
        """, publicRole: false))[0];

    private async Task ApplyAsync(IReadOnlyList<MigrationOperation> operations)
    {
        await using var connection = new NpgsqlConnection(database);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        foreach (var operation in operations)
        {
            await using var command = new NpgsqlCommand(Assert.IsType<SqlOperation>(operation).Sql, connection, transaction);
            await command.ExecuteNonQueryAsync();
        }
        await transaction.CommitAsync();
    }

    private Task<string[]> QueryAsPublicAsync(string sql) => QueryAsync(sql, publicRole: true);

    private async Task<string[]> QueryAsync(string sql, bool publicRole)
    {
        await using var connection = new NpgsqlConnection(database);
        await connection.OpenAsync();
        if (publicRole)
            await using (var role = new NpgsqlCommand("SET ROLE civic_lens_public", connection)) await role.ExecuteNonQueryAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<string>();
        while (await reader.ReadAsync()) rows.Add(reader.GetString(0));
        return [.. rows];
    }
}
