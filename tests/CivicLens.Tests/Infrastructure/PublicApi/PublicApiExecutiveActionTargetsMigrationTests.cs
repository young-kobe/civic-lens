using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Collection.Migrations;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Infrastructure.Collection;
using CivicLens.Tests.PublicApi.Votes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
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
            $"0|Amends|ExecutiveOrder|14100|8:25|{ExecutiveActionTargetRows.LinkedOrder}",
            "1|Amends|ExecutiveOrder|14101|35:8|",
            "2|Amends|ExecutiveOrder|14102|45:8|",
            $"3|Revokes|Proclamation|10001|63:11|{ExecutiveActionTargetRows.LinkedProclamation}",
            "4|Revokes|ExecutiveOrder|14000|76:8|"
        ], await QueryAsPublicAsync($"""
            SELECT "position" || '|' || relation || '|' || kind || '|' || number || '|' || reference_start || ':' || reference_length || '|' || coalesce(target_record_id, '')
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
    public async Task AnInactiveSourceRecordHasNoTargetRowsEvenWhenItStillStoresTargets()
    {
        await ViewsSeed.ExecuteAsync(database, $"UPDATE publication_record_states SET last_release = 1 WHERE record_id = '{ExecutiveActionTargetRows.Source}'");

        Assert.Equal(["0"], await QueryAsPublicAsync("SELECT count(*)::text FROM public_api.executive_action_targets"));
        Assert.Equal(["5"], await QueryAsync("SELECT count(*)::text FROM executive_action_revision_disposition_targets WHERE position < 5", publicRole: false));
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
        Assert.Equal(["0"], await QueryAsync("SELECT count(*)::text FROM pg_indexes WHERE indexname = 'IX_executive_action_revisions_kind_presidential_number'", publicRole: false));

        await ApplyAsync(migration.UpOperations);
        await using var db = new CollectionAttemptDbContext(new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(database).Options);
        var grants = typeof(PublicApiExecutiveActionTargets).Assembly.GetType("CivicLens.Infrastructure.Publication.PublicApiGrants")!;
        await (Task)grants.GetMethod("ApplyAsync")!.Invoke(null, [db, CancellationToken.None])!;
        Assert.Equal("true|true|false", await GrantStateAsync());
        Assert.Equal(["1"], await QueryAsync("SELECT count(*)::text FROM pg_indexes WHERE indexname = 'IX_executive_action_revisions_kind_presidential_number'", publicRole: false));
        Assert.Equal(["5"], await QueryAsPublicAsync("SELECT count(*)::text FROM public_api.executive_action_targets"));
    }

    private async Task<string> GrantStateAsync() => (await QueryAsync("""
        SELECT has_column_privilege('civic_lens_public_owner', 'executive_action_revisions', 'disposition_notes', 'SELECT')::text
            || '|' || has_any_column_privilege('civic_lens_public_owner', 'executive_action_revision_disposition_targets', 'SELECT')::text
            || '|' || (to_regclass('public_api.executive_action_targets') IS NULL)::text
        """, publicRole: false))[0];

    private async Task ApplyAsync(IReadOnlyList<MigrationOperation> operations)
    {
        await using var db = new CollectionAttemptDbContext(new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(database).Options);
        var commands = db.GetService<IMigrationsSqlGenerator>().Generate(operations, db.Model);
        await using var connection = new NpgsqlConnection(database);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        foreach (var statement in commands)
        {
            await using var command = new NpgsqlCommand(statement.CommandText, connection, transaction);
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
