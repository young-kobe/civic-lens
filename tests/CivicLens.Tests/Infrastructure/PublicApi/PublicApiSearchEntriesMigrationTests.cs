using System.Globalization;
using CivicLens.Application.Publication;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Collection.Migrations;
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
public sealed class PublicApiSearchEntriesMigrationTests(PostgresCollection postgres)
{
    private const string Migration = "20261112050100_PublicApiSearchEntries";
    private static readonly string[] Views = ["record_entries", "record_first_publications", "record_issues", "record_officials", "record_policy_areas"];
    private static readonly DateTimeOffset FirstPublished = new(2026, 3, 2, 12, 0, 0, TimeSpan.Zero);
    private static readonly string ActionId = new('a', 64);
    private static readonly string RetiredId = new('b', 32);

    private static readonly PublishedRecordEntry OldAction = PublishedEntryFixtures.Action(ActionId, FirstPublished) with { BillLine = "H.R. 1, introduced" };

    private static readonly PublishedRecordEntry NewAction = PublishedEntryFixtures.Action(ActionId, FirstPublished, revision: 2) with
    {
        Headline = "A newer bill headline",
        Officials = [new() { Id = "S000003", Name = "Sam Third" }, new() { Id = "S000001", Name = "Sam First" }],
        IssueIds = ["housing", "budget", "tax"],
        PolicyAreaIds = ["housing", "economics"],
        SourceHost = "api.congress.gov",
        EventDate = new DateOnly(2026, 4, 1),
        BillLine = "H.R. 1, passed House"
    };

    private static readonly PublishedRecordEntry Retired =
        PublishedEntryFixtures.DocumentChange(RetiredId, 1, "A page changed", FirstPublished.AddDays(-3));

    [Fact]
    public async Task EachEntryViewShowsTheStoredEntryOfTheActiveRevisionAndARollbackMovesItBecauseTheViewsJoinActiveRecords()
    {
        var database = await postgres.CreateMigratedDatabaseAsync();
        try
        {
            await SeedAsync(database);

            Assert.Equal(Expected(NewAction), await EntryAsync(database));

            await ViewsSeed.ActivateReleaseAsync(database, 1, DateTimeOffset.UnixEpoch);
            string[] rolledBack = [.. Expected(OldAction), .. Expected(Retired)];
            Assert.Equal(rolledBack, await EntryAsync(database));

            await ExecuteAsync(database, "UPDATE publication_releases SET is_active = false");
            foreach (var view in Views)
                Assert.Empty(await QueryAsPublicAsync(database, $"SELECT record_id FROM public_api.{view}"));
            foreach (var table in new[] { "published_entries", "published_entry_officials", "published_entry_issues", "published_entry_policy_areas", "published_records" })
            {
                var exception = await Assert.ThrowsAsync<PostgresException>(() => QueryAsPublicAsync(database, $"SELECT record_id FROM public.{table}"));
                Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, exception.SqlState);
            }
        }
        finally { await postgres.DropDatabaseAsync(database); }
    }

    [Fact]
    public async Task EachEntryViewCarriesTheBillTitleDocumentTypeLinkColumnsAndMatchedOfficialThatEqualTheManifestEntryOfTheActiveRelease()
    {
        var database = await postgres.CreateMigratedDatabaseAsync();
        try
        {
            string executiveId = new('c', 64), relationshipId = new('d', 64), statementId = new('e', 64);
            var statement = PublishedEntryFixtures.DocumentChange(statementId, 1, "A statement", FirstPublished) with { Kind = PublishedRecordKind.Statement };
            PublishedRecordEntry[] entries = [statement, PublishedEntryFixtures.Action(ActionId, FirstPublished), PublishedEntryFixtures.ExecutiveAction(executiveId, FirstPublished),
                PublishedEntryFixtures.Relationship(relationshipId, statementId, ActionId, FirstPublished), PublishedEntryFixtures.Vote(new string('f', 64), FirstPublished)];
            await PublishedEntryFixtures.SeedEntriesAsync(database, entries, allowMissingRevisionParents: true);
            await ViewsSeed.ActivateReleaseAsync(database, 1, DateTimeOffset.UnixEpoch);
            await ExecuteAsync(database, string.Join('\n', entries.Select(entry =>
                $"INSERT INTO publication_record_states (kind, record_id, first_release, revision_number, last_release) VALUES ('{PublishedRecordReference.NameOf(entry.Kind)}', '{entry.RecordId}', 1, 1, NULL);")));

            string[] expected = [.. entries.OrderBy(entry => entry.RecordId, StringComparer.Ordinal).Select(entry => string.Join('|', entry.RecordId, entry.BillTitle, entry.DocumentType?.ToString(),
                entry.LinkType?.ToString(), entry.StatementRecordId is null ? null : "statement", entry.StatementRecordId, entry.TargetKind is { } target ? PublishedRecordReference.NameOf(target) : null,
                entry.TargetRecordId, string.Join(',', entry.Officials.Select(official => official.Id + ":" + (official.Id == entry.MatchedOfficialId).ToString().ToLowerInvariant()))))];
            Assert.Equal(expected, await QueryAsPublicAsync(database, """
                SELECT concat_ws('|', e.record_id, coalesce(e.bill_title, ''), coalesce(e.document_type, ''), coalesce(e.link_type, ''), coalesce(e.statement_kind, ''),
                       coalesce(e.statement_record_id, ''), coalesce(e.target_kind, ''), coalesce(e.target_record_id, ''),
                       (SELECT coalesce(string_agg(o.official_id || ':' || o.is_matched::text, ',' ORDER BY o."position"), '') FROM public_api.record_officials o
                         WHERE o.kind = e.kind AND o.record_id = e.record_id AND o.revision_number = e.revision_number))
                  FROM public_api.record_entries e ORDER BY e.record_id
                """));
        }
        finally { await postgres.DropDatabaseAsync(database); }
    }

    [Fact]
    public async Task DowngradeDropsTheEntryViewsAndRevokesOnlyTheGrantsThatNoOlderViewReads()
    {
        var database = await postgres.CreateMigratedDatabaseAsync();
        try
        {
            await using var db = new CollectionAttemptDbContext(new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(database).Options);
            var previous = db.Database.GetMigrations().Last(id => string.CompareOrdinal(id, Migration) < 0);
            await db.GetService<IMigrator>().MigrateAsync(previous);

            Assert.Equal(["0"], await QueryAsync(database, $"""
                SELECT count(*)::text FROM pg_class WHERE relnamespace = 'public_api'::regnamespace AND relname IN ({string.Join(", ", Views.Select(view => $"'{view}'"))})
                """));
            Assert.Equal("0", await AddedGrantCountAsync(database));
            Assert.Equal(["true|true|true|true"], await QueryAsync(database, """
                SELECT has_column_privilege('civic_lens_public_owner', 'published_entries', 'event_date', 'SELECT')::text || '|'
                    || has_column_privilege('civic_lens_public_owner', 'published_entries', 'bill_line', 'SELECT')::text || '|'
                    || has_column_privilege('civic_lens_public_owner', 'published_entry_officials', 'official_id', 'SELECT')::text || '|'
                    || has_column_privilege('civic_lens_public_owner', 'published_records', 'first_published_at_utc_ticks', 'SELECT')::text
                """));

            await db.GetService<IMigrator>().MigrateAsync();
            await ApplyGrantsAsync(database);
            Assert.Equal("15", await AddedGrantCountAsync(database));
            await SeedAsync(database);
            Assert.Equal(Expected(NewAction), await EntryAsync(database));
        }
        finally { await postgres.DropDatabaseAsync(database); }
    }

    // Release 1 holds revision 1 of the action and the retired document change. Release 2 holds revision 2 of the action only.
    private static async Task SeedAsync(string database)
    {
        await PublishedEntryFixtures.SeedEntriesAsync(database, [OldAction, NewAction, Retired], allowMissingRevisionParents: true);
        await ViewsSeed.ActivateReleaseAsync(database, 1, DateTimeOffset.UnixEpoch);
        await ViewsSeed.ActivateReleaseAsync(database, 2, DateTimeOffset.UnixEpoch.AddDays(1));
        await ExecuteAsync(database, $"""
            INSERT INTO publication_record_states (kind, record_id, first_release, revision_number, last_release) VALUES
              ('action', '{ActionId}', 1, 1, 1), ('action', '{ActionId}', 2, 2, NULL), ('documentChange', '{Retired.RecordId}', 1, 1, 1);
            """);
    }

    // The search ingest rebuilds these fields of an entry from the views, so the views must give them back unchanged and in order.
    private static string[] Expected(PublishedRecordEntry entry)
    {
        var kind = PublishedRecordReference.NameOf(entry.Kind);
        return [string.Join('|', entry.RecordId, kind, entry.RevisionNumber, entry.Headline, entry.SourceHost, entry.EventDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            entry.BillLine, string.Join(',', entry.Officials.Select(official => official.Id)), string.Join(',', entry.IssueIds), string.Join(',', entry.PolicyAreaIds),
            entry.FirstPublishedAtUtc.UtcTicks)];
    }

    private static Task<string[]> EntryAsync(string database) => QueryAsPublicAsync(database, """
        SELECT concat_ws('|', e.record_id, e.kind, e.revision_number, e.headline, coalesce(e.source_host, ''), coalesce(e.event_date::text, ''), coalesce(e.bill_line, ''),
               (SELECT coalesce(string_agg(o.official_id, ',' ORDER BY o."position"), '') FROM public_api.record_officials o
                 WHERE o.kind = e.kind AND o.record_id = e.record_id AND o.revision_number = e.revision_number),
               (SELECT coalesce(string_agg(i.issue_id, ',' ORDER BY i."position"), '') FROM public_api.record_issues i
                 WHERE i.kind = e.kind AND i.record_id = e.record_id AND i.revision_number = e.revision_number),
               (SELECT coalesce(string_agg(p.policy_area_id, ',' ORDER BY p."position"), '') FROM public_api.record_policy_areas p
                 WHERE p.kind = e.kind AND p.record_id = e.record_id AND p.revision_number = e.revision_number),
               f.first_published_at_utc_ticks)
          FROM public_api.record_entries e
          JOIN public_api.record_first_publications f ON f.kind = e.kind AND f.record_id = e.record_id
         ORDER BY e.record_id
        """);

    private static async Task<string> AddedGrantCountAsync(string database) => (await QueryAsync(database, """
        SELECT count(*)::text FROM (VALUES
            ('published_entries', 'headline'), ('published_entries', 'source_host'), ('published_entries', 'bill_title'),
            ('published_entry_officials', 'position'), ('published_entry_officials', 'is_matched'),
            ('published_entry_issues', 'kind'), ('published_entry_issues', 'record_id'), ('published_entry_issues', 'revision_number'),
            ('published_entry_issues', 'position'), ('published_entry_issues', 'issue_id'),
            ('published_entry_policy_areas', 'kind'), ('published_entry_policy_areas', 'record_id'), ('published_entry_policy_areas', 'revision_number'),
            ('published_entry_policy_areas', 'position'), ('published_entry_policy_areas', 'policy_area_id')
        ) added(table_name, column_name)
        WHERE has_column_privilege('civic_lens_public_owner', added.table_name, added.column_name, 'SELECT')
        """))[0];

}
