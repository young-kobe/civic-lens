using CivicLens.Infrastructure.Collection;
using CivicLens.Tests.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.PublicApi;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class VoteMemberListEntriesCoveringIndexMigrationTests(PostgresCollection postgres)
{
    private const string Migration = "20261114230000_VoteMemberListEntriesCoveringIndex";

    [Fact]
    public async Task TheListIndexCoversEveryEntryColumnOfThePublicListReadsAndTheRollbackRestoresTheTalliesIndex()
    {
        var database = await postgres.CreateMigratedDatabaseAsync();
        try
        {
            await using var db = new CollectionAttemptDbContext(new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(database).Options);
            var previous = db.Database.GetMigrations().Last(id => string.CompareOrdinal(id, Migration) < 0);
            Assert.Equal(["CREATE INDEX ix_vote_member_list_entries_members ON public.vote_member_list_entries USING btree (list_id) INCLUDE (person_id, party, vote_cast)"],
                await ListIndexDefinitionsAsync(database));
            Assert.Empty(await UncoveredEntryColumnsAsync(database));

            await db.GetService<IMigrator>().MigrateAsync(previous);
            Assert.Equal(["CREATE INDEX ix_vote_member_list_entries_tallies ON public.vote_member_list_entries USING btree (list_id) INCLUDE (vote_cast)"],
                await ListIndexDefinitionsAsync(database));

            await PostgresCollectionAttemptStore.FromConnectionString(database).MigrateAsync();
            Assert.Equal(["ix_vote_member_list_entries_members"], (await ListIndexDefinitionsAsync(database)).Select(IndexName));
        }
        finally { await postgres.DropDatabaseAsync(database); }
    }

    private static string IndexName(string definition) => definition.Split(' ')[2];

    private static Task<string[]> ListIndexDefinitionsAsync(string database) => ReadAsync(database, """
        SELECT pg_get_indexdef(i.indexrelid)
          FROM pg_index i
          JOIN pg_class c ON c.oid = i.indexrelid
         WHERE i.indrelid = 'public.vote_member_list_entries'::regclass
           AND c.relname IN ('ix_vote_member_list_entries_tallies', 'ix_vote_member_list_entries_members')
         ORDER BY c.relname
        """);

    // The entry columns that the list reads of member_votes and official_issue_counts reference, minus the columns of the list index.
    private static Task<string[]> UncoveredEntryColumnsAsync(string database) => ReadAsync(database, """
        SELECT DISTINCT a.attname::text
          FROM pg_depend d
          JOIN pg_rewrite r ON r.oid = d.objid
          JOIN pg_attribute a ON a.attrelid = d.refobjid AND a.attnum = d.refobjsubid
         WHERE d.refobjid = 'public.vote_member_list_entries'::regclass
           AND r.ev_class IN ('public_api.member_votes'::regclass, 'public_api.official_issue_counts'::regclass)
           AND a.attnum <> ALL ((SELECT i.indkey::int2[] FROM pg_index i
                                  WHERE i.indexrelid = 'public.ix_vote_member_list_entries_members'::regclass)::int2[])
         ORDER BY 1
        """);

    private static async Task<string[]> ReadAsync(string database, string sql)
    {
        await using var connection = new NpgsqlConnection(database);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        var values = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) values.Add(reader.GetString(0));
        return [.. values];
    }
}
