using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CivicLens.Application.Publication;
using CivicLens.Application.Votes;
using CivicLens.Core.Actions;
using CivicLens.Core.Documents;
using CivicLens.Core.Votes;
using CivicLens.Infrastructure.Districts;
using CivicLens.Infrastructure.Roster;
using CivicLens.PublicWeb.Publication;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Application.Publication;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Infrastructure.Collection;
using CivicLens.Tests.PublicApi.Bills;
using CivicLens.Tests.PublicApi.Votes;
using CivicLens.Infrastructure.Collection;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.PublicApi;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PublicApiViewsTests(PostgresCollection postgres) : IAsyncLifetime
{
    private const string Role = "civic_lens_public";
    private const string Owner = "civic_lens_public_owner";
    private const string LoginPassword = "test-only-password";

    private static readonly string[] BaseTables =
        ["vote_revisions", "vote_revision_party_tallies", "vote_revision_facts", "vote_member_list_entries", "vote_revision_issues", "publication_releases", "publication_record_states", "vote_drafts", "vote_member_lists",
         "brief_revisions", "relationship_revisions", "action_revisions", "action_revision_sponsorships", "action_revision_issues", "action_drafts", "published_entries", "zcta_districts", "roster_snapshots", "collection_attempts",
         "brief_revision_paragraphs", "brief_revision_citations", "brief_revision_paragraph_citations", "document_change_revisions",
         "document_comparisons", "document_comparison_hunks", "document_comparison_word_edits", "document_change_revision_citations",
         "document_change_revision_issues", "document_change_revision_officials",
         "document_extractions", "statement_revision_quotations",
         "release_officials", "release_official_terms", "release_issues", "release_issue_includes", "release_issue_excludes", "release_issue_party_lineups", "release_issue_party_counts", "release_policy_areas", "published_entry_officials",
         "published_bill_histories", "published_bill_history_actions", "release_bill_histories",
         "release_bill_statuses", "statement_review_decisions", "statement_revisions", "statement_revision_speakers", "statement_revision_issues",
         "statement_drafts", "statement_candidates", "executive_action_revisions", "executive_action_revision_notices", "executive_action_documents",
         "publication_withdrawals", "publication_withdrawal_corrections", "published_records"];

    private string connectionString = null!;
    private string[] roleAfterMigration = null!;

    public async Task InitializeAsync()
    {
        connectionString = await postgres.CreateMigratedDatabaseAsync();
        roleAfterMigration = await QueryAsync($"""
            SELECT rolcanlogin || ' ' || rolconnlimit || ' ' || (rolpassword IS NULL) || ' ' || rolsuper || ' ' || rolcreaterole || ' ' || rolcreatedb
              FROM pg_authid WHERE rolname = '{Role}'
            """, connectionString);
        await ExecuteAsync($"ALTER ROLE {Role} LOGIN PASSWORD '{LoginPassword}'");
    }

    // The role belongs to the cluster, so each test puts it back to the state that the migration leaves.
    public async Task DisposeAsync()
    {
        await ExecuteAsync($"ALTER ROLE {Role} NOLOGIN PASSWORD NULL");
        await postgres.DropDatabaseAsync(connectionString);
    }

    [Fact]
    public async Task TheRoleReadsNoBaseTableAndWritesNothingBecauseItHoldsOnlyTheViewGrants()
    {
        await using var connection = await OpenPublicAsync();
        // The session can lift the read-only default itself, so the grants must hold alone.
        await using (var lift = new NpgsqlCommand("SET default_transaction_read_only = off", connection)) await lift.ExecuteNonQueryAsync();
        foreach (var table in BaseTables)
            await AssertDeniedAsync(connection, $"SELECT * FROM public.{table}");
        Assert.Equal(["false"], await QueryAsync($"SELECT has_any_column_privilege('{Owner}', 'public.statement_review_decisions', 'SELECT')::text", connectionString));
        await AssertDeniedAsync(connection, "SELECT count(*) FROM public.vote_revisions");
        await AssertDeniedAsync(connection, "INSERT INTO public.publication_releases (release_number) VALUES (9)");
        await AssertDeniedAsync(connection, "UPDATE public.vote_revisions SET headline = 'x'");
        await AssertDeniedAsync(connection, "DELETE FROM public.publication_record_states");
        await AssertDeniedAsync(connection, "SELECT pg_advisory_lock(1)");
        await AssertDeniedAsync(connection, "SELECT pg_try_advisory_lock(1)");
        await AssertDeniedAsync(connection, "SELECT pg_try_advisory_xact_lock(1)");
        foreach (var view in new[] { "active_records", "active_release", "votes", "vote_members", "vote_first_publications", "vote_party_tallies", "member_votes", "vote_issues", "party_agreement", "bills", "bill_sponsorships", "bill_issues", "zip_districts", "officials", "official_attendance", "official_party_lines", "official_bill_counts", "official_issue_counts", "official_executive_actions", "release_officials", "release_official_terms", "release_issues", "release_issue_includes", "release_issue_excludes", "release_issue_party_lineups", "release_policy_areas",
            "briefs", "issue_votes", "issue_sponsors", "bill_status", "record_headlines", "record_links", "vote_facts", "statement_speakers", "statements", "statement_sources", "statement_quotations", "document_changes", "document_change_officials", "document_change_sources", "document_change_hunks", "document_change_word_edits", "document_change_citations",
            "executive_actions", "executive_action_notices", "withdrawals", "withdrawal_corrections" })
        {
            // Join and aggregate views reject writes before privilege checks. The simple release identity view reaches the missing-grant check.
            var sqlState = view == "active_release" ? "42501" : "55000";
            await AssertDeniedAsync(connection, $"DELETE FROM public_api.{view}", sqlState);
            await AssertDeniedAsync(connection, $"UPDATE public_api.{view} SET {WritableProbeColumn(view)}", sqlState);
        }
    }

    [Fact]
    public async Task EveryViewColumnIsInTheCheckedInListBecauseANewColumnNeedsAVisibleEdit()
    {
        var path = Path.Combine(RepositoryRoot.FullName, "docs", "architecture", "public-api-columns.txt");
        var listed = File.ReadAllLines(path).Where(line => line.Length > 0 && line[0] != '#').Order(StringComparer.Ordinal).ToArray();
        var found = (await QueryAsync("""
            SELECT table_name || ' ' || column_name FROM information_schema.columns WHERE table_schema = 'public_api'
            """, connectionString)).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(listed, found);
    }

    [Fact]
    public async Task EveryObjectOfThePublicApiSchemaIsAViewOfTheOwnerWithOneSelectGrantToTheRoleAndNoOtherGrantee()
    {
        var objects = await QueryAsync("""
            SELECT c.relname || ' ' || c.relkind::text || ' ' || pg_get_userbyid(c.relowner) || ' '
                   || coalesce((SELECT string_agg(pg_get_userbyid(g.grantee) || ':' || g.privilege_type, ',' ORDER BY g.grantee)
                                  FROM aclexplode(c.relacl) g WHERE g.grantee <> c.relowner), 'none') || ' '
                   || coalesce(c.reloptions::text, 'none')
              FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace WHERE n.nspname = 'public_api' ORDER BY c.relname
            """, connectionString);
        Assert.Equal(
        [
            $"active_records v {Owner} {Role}:SELECT {{security_barrier=true}}",
            $"active_release v {Owner} {Role}:SELECT {{security_barrier=true}}",
            $"bill_actions v {Owner} {Role}:SELECT {{security_barrier=true}}",
            $"bill_issues v {Owner} {Role}:SELECT {{security_barrier=true}}",
            $"bill_sponsorships v {Owner} {Role}:SELECT {{security_barrier=true}}",
            $"bill_status v {Owner} {Role}:SELECT {{security_barrier=true}}",
            $"bills v {Owner} {Role}:SELECT {{security_barrier=true}}",
            $"brief_citations v {Owner} {Role}:SELECT {{security_barrier=true}}",
            $"brief_paragraphs v {Owner} {Role}:SELECT {{security_barrier=true}}",
            $"briefs v {Owner} {Role}:SELECT {{security_barrier=true}}",
            $"document_change_citations v {Owner} {Role}:SELECT {{security_barrier=true}}",
            $"document_change_hunks v {Owner} {Role}:SELECT {{security_barrier=true}}",
            $"document_change_officials v {Owner} {Role}:SELECT {{security_barrier=true}}",
            $"document_change_sources v {Owner} {Role}:SELECT {{security_barrier=true}}",
            $"document_change_word_edits v {Owner} {Role}:SELECT {{security_barrier=true}}",
            $"document_changes v {Owner} {Role}:SELECT {{security_barrier=true}}",
            $"executive_action_notices v {Owner} {Role}:SELECT {{security_barrier=true}}",
            $"executive_actions v {Owner} {Role}:SELECT {{security_barrier=true}}",
            $"issue_sponsors v {Owner} {Role}:SELECT {{security_barrier=true}}",
            $"issue_votes v {Owner} {Role}:SELECT {{security_barrier=true}}",
            $"member_votes v {Owner} {Role}:SELECT {{security_barrier=true}}",
            $"official_attendance v {Owner} {Role}:SELECT {{security_barrier=true}}",
            $"official_bill_counts v {Owner} {Role}:SELECT {{security_barrier=true}}",
            $"official_executive_actions v {Owner} {Role}:SELECT {{security_barrier=true}}",
            $"official_issue_counts v {Owner} {Role}:SELECT {{security_barrier=true}}",
            $"official_party_lines v {Owner} {Role}:SELECT {{security_barrier=true}}",
            $"officials v {Owner} {Role}:SELECT {{security_barrier=true}}",
            $"party_agreement v {Owner} {Role}:SELECT {{security_barrier=true}}",
            $"record_headlines v {Owner} {Role}:SELECT {{security_barrier=true}}",
            $"record_links v {Owner} {Role}:SELECT {{security_barrier=true}}",
            $"release_issue_excludes v {Owner} {Role}:SELECT {{security_barrier=true}}",
            $"release_issue_includes v {Owner} {Role}:SELECT {{security_barrier=true}}",
            $"release_issue_party_lineups v {Owner} {Role}:SELECT {{security_barrier=true}}",
            $"release_issues v {Owner} {Role}:SELECT {{security_barrier=true}}",
            $"release_official_terms v {Owner} {Role}:SELECT {{security_barrier=true}}",
            $"release_officials v {Owner} {Role}:SELECT {{security_barrier=true}}",
            $"release_policy_areas v {Owner} {Role}:SELECT {{security_barrier=true}}",
            $"statement_quotations v {Owner} {Role}:SELECT {{security_barrier=true}}",
            $"statement_sources v {Owner} {Role}:SELECT {{security_barrier=true}}",
            $"statement_speakers v {Owner} {Role}:SELECT {{security_barrier=true}}",
            $"statements v {Owner} {Role}:SELECT {{security_barrier=true}}",
            $"vote_facts v {Owner} {Role}:SELECT {{security_barrier=true}}",
            $"vote_first_publications v {Owner} {Role}:SELECT {{security_barrier=true}}",
            $"vote_issues v {Owner} {Role}:SELECT {{security_barrier=true}}",
            $"vote_members v {Owner} {Role}:SELECT {{security_barrier=true}}",
            $"vote_party_tallies v {Owner} {Role}:SELECT {{security_barrier=true}}",
            $"votes v {Owner} {Role}:SELECT {{security_barrier=true}}",
            $"withdrawal_corrections v {Owner} {Role}:SELECT {{security_barrier=true}}",
            $"withdrawals v {Owner} {Role}:SELECT {{security_barrier=true}}",
            $"zip_districts v {Owner} {Role}:SELECT {{security_barrier=true}}"
        ], objects);
        Assert.Empty(await QueryAsync("SELECT proname FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace WHERE n.nspname = 'public_api'", connectionString));
        var schema = await QueryAsync("""
            SELECT pg_get_userbyid(n.nspowner) || ' ' || (SELECT string_agg(coalesce(nullif(g.grantee, 0)::regrole::text, 'PUBLIC') || ':' || g.privilege_type, ',')
                                                           FROM aclexplode(n.nspacl) g WHERE g.grantee <> n.nspowner)
              FROM pg_namespace n WHERE n.nspname = 'public_api'
            """, connectionString);
        Assert.Equal([$"{Owner} {Role}:USAGE"], schema);
    }

    [Fact]
    public async Task TheRoleHoldsNoPrivilegeOutsideThePublicApiSchemaBecauseNothingElseMayBeReachable()
    {
        await ExecuteAsync(ReadOnlyGrantsScript());
        var privileges = "'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER'";
        var columnPrivileges = "'SELECT,INSERT,UPDATE,REFERENCES'";
        var relations = await QueryAsync($"""
            SELECT n.nspname || '.' || c.relname FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
             WHERE n.nspname NOT IN ('pg_catalog', 'information_schema', 'public_api') AND c.relkind IN ('r', 'p', 'v', 'm', 'S', 'f')
               AND (has_table_privilege('{Role}', c.oid, {privileges}) OR has_any_column_privilege('{Role}', c.oid, {columnPrivileges}))
             ORDER BY 1
            """, connectionString);
        Assert.Empty(relations);
        Assert.Empty(await QueryAsync($"SELECT roleid::regrole::text FROM pg_auth_members WHERE member = '{Role}'::regrole", connectionString));
        Assert.Empty(await QueryAsync($"""
            SELECT n.nspname || '.' || p.proname FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace
             WHERE n.nspname NOT IN ('pg_catalog', 'information_schema', 'public_api') AND p.prorettype <> 'trigger'::regtype AND has_function_privilege('{Role}', p.oid, 'EXECUTE') ORDER BY 1
            """, connectionString));
    }

    [Fact]
    public async Task AMigratedDatabaseGrantsNoTemporaryTablesToPublicAndKeepsConnectForTheRoleBecauseTheDatabaseGrantsDoNotSurviveATemplateCopy()
    {
        var empty = await postgres.CreateEmptyDatabaseAsync();
        try
        {
            await PostgresCollectionAttemptStore.FromConnectionString(empty).MigrateAsync();

            Assert.Equal(["false true"], await QueryAsync($"SELECT has_database_privilege('{Role}', current_database(), 'TEMPORARY') || ' ' || has_database_privilege('{Role}', current_database(), 'CONNECT')", empty));
        }
        finally
        {
            await postgres.DropDatabaseAsync(empty);
        }
    }

    [Fact]
    public async Task TheOwnerHoldsGrantsOnlyOnColumnsThatAViewReadsBecauseASpareColumnGrantWouldWidenWhatAViewCanShow()
    {
        Assert.Empty(await QueryAsync($"""
            SELECT c.relname || '.' || a.attname FROM pg_attribute a
              JOIN pg_class c ON c.oid = a.attrelid JOIN pg_namespace n ON n.oid = c.relnamespace
             WHERE n.nspname = 'public' AND a.attnum > 0
               AND EXISTS (SELECT 1 FROM aclexplode(a.attacl) g WHERE g.grantee = '{Owner}'::regrole)
               AND NOT EXISTS (SELECT 1 FROM pg_depend d JOIN pg_rewrite r ON r.oid = d.objid
                                 JOIN pg_class v ON v.oid = r.ev_class JOIN pg_namespace vn ON vn.oid = v.relnamespace
                                WHERE vn.nspname = 'public_api' AND d.refobjid = a.attrelid AND d.refobjsubid = a.attnum)
            UNION ALL
            SELECT c.relname FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
             WHERE n.nspname = 'public' AND EXISTS (SELECT 1 FROM aclexplode(c.relacl) g WHERE g.grantee = '{Owner}'::regrole)
            ORDER BY 1
            """, connectionString));
    }

    [Fact]
    public async Task TheRoleIsReadOnlyShortLivedAndLimitedAndTheMigrationLeavesItWithoutLoginOrPassword()
    {
        Assert.Equal(["false 5 true false false false"], roleAfterMigration);
        var settings = await QueryAsync($"SELECT unnest(setconfig) FROM pg_db_role_setting WHERE setrole = '{Role}'::regrole ORDER BY 1", connectionString);
        Assert.Equal(["default_transaction_read_only=on", "idle_in_transaction_session_timeout=10s", "idle_session_timeout=60s", "statement_timeout=2s"], settings);
        var owner = await QueryAsync($"SELECT rolcanlogin FROM pg_roles WHERE rolname = '{Owner}'", connectionString);
        Assert.Equal(["False"], owner);
    }

    [Fact]
    public async Task OnlyRecordsOfTheActiveReleaseAppearAndARollbackMovesTheViewsAtOnce()
    {
        await SeedAsync();
        Assert.Equal(["a", "c", "d"], await PublicAsync("SELECT DISTINCT record_id FROM public_api.votes ORDER BY 1"));
        Assert.Equal(["a 2 2", "c 1 2", "d 1 2"], await PublicAsync("SELECT record_id || ' ' || revision_number || ' ' || release_number FROM public_api.votes ORDER BY 1"));
        Assert.Equal(["2"], await PublicAsync("SELECT DISTINCT release_number FROM public_api.active_records"));
        Assert.Equal(["a", "c", "d"], await PublicAsync("SELECT DISTINCT record_id FROM public_api.vote_members ORDER BY 1"));
        Assert.Equal(["a", "c", "d"], await PublicAsync("SELECT DISTINCT record_id FROM public_api.member_votes ORDER BY 1"));
        Assert.Equal(["d"], await PublicAsync("SELECT DISTINCT record_id FROM public_api.vote_issues"));

        await ExecuteAsync("UPDATE publication_releases SET is_active = false; UPDATE publication_releases SET is_active = true WHERE release_number = 1");

        Assert.Equal(["a 1 1", "b 1 1", "d 1 1"], await PublicAsync("SELECT record_id || ' ' || revision_number || ' ' || release_number FROM public_api.votes ORDER BY 1"));
        Assert.Equal(["a", "b", "d"], await PublicAsync("SELECT DISTINCT record_id FROM public_api.vote_members ORDER BY 1"));
        Assert.Equal(["a", "b", "d"], await PublicAsync("SELECT DISTINCT record_id FROM public_api.member_votes ORDER BY 1"));

        await ExecuteAsync("UPDATE publication_releases SET is_active = false");
        Assert.Empty(await PublicAsync("SELECT record_id FROM public_api.votes"));
        Assert.Empty(await PublicAsync("SELECT record_id FROM public_api.active_records"));
    }

    [Fact]
    public async Task VotesLinkOnlyToTheLowestIdMatchingBillInTheActivePublishedRelease()
    {
        await ViewsSeed.ActivateReleaseAsync(connectionString, 1, new DateTimeOffset(2026, 6, 2, 0, 0, 0, TimeSpan.Zero));
        await ViewsSeed.AddVotesAsync(connectionString,
            new SeededVote('7', "2026-03-03") { BillNumber = 7 },
            new SeededVote('8', "2026-03-02") { BillNumber = 8 },
            new SeededVote('9', "2026-03-01") { BillType = null, BillNumber = null });
        await BillsSeed.AddBillsAsync(connectionString,
            new SeededBill('b', "2026-01-01") { BillNumber = 7, LastRelease = 1 },
            new SeededBill('d', "2026-01-01") { BillNumber = 7, LastRelease = 1 },
            new SeededBill('e', "2026-01-01") { BillNumber = 7, FirstRelease = 2 },
            new SeededBill('0', "2026-01-01") { BillNumber = 7, Congress = 118 },
            new SeededBill('1', "2026-01-01") { BillNumber = 7, BillType = "s" });

        Assert.Equal(["7 b", "8 none", "9 none"], await PublicAsync("SELECT left(v.record_id, 1) || ' ' || coalesce(left(v.bill_record_id, 1), 'none') FROM public_api.votes v ORDER BY v.record_id"));

        await ViewsSeed.ActivateReleaseAsync(connectionString, 2, new DateTimeOffset(2026, 6, 9, 0, 0, 0, TimeSpan.Zero));
        Assert.Equal(["7 e", "8 none", "9 none"], await PublicAsync("SELECT left(v.record_id, 1) || ' ' || coalesce(left(v.bill_record_id, 1), 'none') FROM public_api.votes v ORDER BY v.record_id"));

        await ViewsSeed.ActivateReleaseAsync(connectionString, 1, new DateTimeOffset(2026, 6, 2, 0, 0, 0, TimeSpan.Zero));
        Assert.Equal(["7 b", "8 none", "9 none"], await PublicAsync("SELECT left(v.record_id, 1) || ' ' || coalesce(left(v.bill_record_id, 1), 'none') FROM public_api.votes v ORDER BY v.record_id"));
    }

    [Fact]
    public async Task TheActiveReleaseIdentitySurvivesAnEmptyReleaseAndTheRoleReadsNoOtherReleaseColumn()
    {
        await SeedAsync();
        Assert.Equal(["2 2"], await PublicAsync("SELECT DISTINCT release_number || ' ' || published_at_utc_ticks FROM public_api.active_records"));

        await ExecuteAsync("UPDATE publication_releases SET is_active = false; UPDATE publication_releases SET is_active = true WHERE release_number = 1");

        Assert.Equal(["1 1"], await PublicAsync("SELECT DISTINCT release_number || ' ' || published_at_utc_ticks FROM public_api.active_records"));
        await ExecuteAsync("UPDATE publication_releases SET is_active = false; UPDATE publication_releases SET is_active = true WHERE release_number = 2; DELETE FROM publication_record_states WHERE first_release = 2; UPDATE publication_record_states SET last_release = 1 WHERE first_release = 1 AND last_release IS NULL; UPDATE publication_releases SET record_count = 0 WHERE release_number = 2");
        Assert.Empty(await PublicAsync("SELECT record_id FROM public_api.active_records"));
        Assert.Equal(["2 2"], await PublicAsync("SELECT release_number || ' ' || published_at_utc_ticks FROM public_api.active_release"));
        await using var connection = await OpenPublicAsync();
        await AssertDeniedAsync(connection, "SELECT is_active FROM public_api.active_release", "42703");
        foreach (var column in new[] { "published_at_utc_ticks", "actor_subject", "directory_name", "record_count", "is_active" })
            await AssertDeniedAsync(connection, $"SELECT {column} FROM public.publication_releases");
    }

    [Fact]
    public async Task TheZipViewShowsNoRowsBeforeTheFirstSnapshotAndHidesCaptureIdentifiers()
    {
        Assert.Empty(await PublicAsync("SELECT zcta FROM public_api.zip_districts"));
        await using var connection = await OpenPublicAsync();
        await AssertDeniedAsync(connection, "SELECT attempt_id FROM public_api.zip_districts", "42703");
        await AssertDeniedAsync(connection, "SELECT capture_sha256 FROM public_api.zip_districts", "42703");
    }

    [Fact]
    public async Task TheZipViewFollowsTheRosterRuleForTheNewestSnapshotWhenTheFileChangesAndChangesBack()
    {
        var steps = new ZipSnapshotSteps(this);
        await steps.AdmitAsync('c', 0, 3);
        await steps.AssertViewMatchesRosterAsync(3);

        await steps.AdmitAsync('d', 1, 2);
        await steps.AssertViewMatchesRosterAsync(2);

        await steps.ImportOnlyAsync('c', 2);
        await steps.AssertViewMatchesRosterAsync(3);
    }

    [Fact]
    public async Task TheZipViewReadsTheOldestSnapshotWhenTwoSnapshotsHoldOneBodyLikeTheRosterRule()
    {
        var steps = new ZipSnapshotSteps(this);
        var first = await steps.ImportOnlyAsync('c', 0);
        var raced = await steps.ImportOnlyAsync('c', 1);
        await steps.AdmitImportedAsync(raced, 1);
        await steps.AdmitImportedAsync(first, 3);

        await steps.AssertViewMatchesRosterAsync(3);
    }

    [Fact]
    public async Task TheZipViewIgnoresSnapshotsOfOtherSourcesAndShowsOneRowForEachZctaStateAndDistrict()
    {
        var steps = new ZipSnapshotSteps(this);
        await steps.AdmitAsync('c', 0, 5);

        Assert.Equal(["20001 DC 0", "36009 AL 1", "36009 AL 2", "94505 CA 14", "99501 AK 0"],
            await PublicAsync("SELECT zcta || ' ' || state || ' ' || district FROM public_api.zip_districts ORDER BY 1"));
    }

    [Fact]
    public async Task TheZipViewGivesTheLandOfTheOverlapAndTheShareOfTheZctaLandSoALargestFirstPageNeedsNoMoreMath()
    {
        var steps = new ZipSnapshotSteps(this);
        await steps.AdmitAsync('c', 0, 5);
        var districtOne = 4938464m;
        var districtTwo = 412306983m;

        var shares = await PublicAsync("""
            SELECT district || ' ' || land_area_square_meters || ' ' || round(land_share, 6) FROM public_api.zip_districts
             WHERE zcta = '36009' ORDER BY land_share DESC
            """);

        Assert.Equal(
        [
            $"2 412306983 {Math.Round(districtTwo / (districtOne + districtTwo), 6)}",
            $"1 4938464 {Math.Round(districtOne / (districtOne + districtTwo), 6)}"
        ], shares);
        Assert.Equal(["1.000000"], await PublicAsync("SELECT round(land_share, 6) FROM public_api.zip_districts WHERE zcta = '99501'"));
        Assert.Empty(await PublicAsync("SELECT zcta FROM public_api.zip_districts GROUP BY zcta HAVING abs(sum(land_share) - 1) > 0.000001"));
    }

    [Fact]
    public async Task TheZipViewNamesTheCongressOfTheDistrictFileSoARouteCanFilterByIt()
    {
        var steps = new ZipSnapshotSteps(this);
        await steps.AdmitAsync('c', 0, 5);

        Assert.Equal(["119"], await PublicAsync("SELECT DISTINCT congress FROM public_api.zip_districts"));
    }

    [Fact]
    public async Task VoteCountsEqualTheMemberRowsAndEveryAiOrCodeRowHasItsLabel()
    {
        await SeedAsync();
        Assert.Equal(["a 2 2 1 2 7"], await PublicAsync("SELECT record_id || ' ' || yea || ' ' || nay || ' ' || present || ' ' || not_voting || ' ' || (yea + nay + present + not_voting) FROM public_api.votes WHERE record_id = 'a'"));
        Assert.Empty(await PublicAsync("""
            SELECT v.record_id FROM public_api.votes v
             WHERE v.yea + v.nay + v.present + v.not_voting <> (SELECT count(*) FROM public_api.vote_members m WHERE m.record_id = v.record_id AND m.revision_number = v.revision_number)
            """));
        Assert.Empty(await PublicAsync("SELECT record_id FROM public_api.votes WHERE text_origin IN ('Model', 'Code') AND label IS NULL"));
        Assert.Equal(
        [
            $"a Code {PublishedRecordLabels.CodeWording}", $"c Person none", $"d Model {PublishedRecordLabels.ModelWording}"
        ], await PublicAsync("SELECT record_id || ' ' || text_origin || ' ' || coalesce(label, 'none') FROM public_api.votes ORDER BY 1"));
        Assert.Equal([$"a Code {PublishedRecordLabels.CodeWording}", "c Person none", $"d Model {PublishedRecordLabels.ModelWording}"],
            await PublicAsync("SELECT DISTINCT m.record_id || ' ' || v.text_origin || ' ' || coalesce(m.label, 'none') FROM public_api.member_votes m JOIN public_api.votes v ON v.record_id = m.record_id ORDER BY 1"));
        Assert.Equal(["0 0 0 1"], await PublicAsync("SELECT yea || ' ' || nay || ' ' || present || ' ' || not_voting FROM public_api.votes WHERE record_id = 'd'"));
    }

    [Fact]
    public async Task PublishedVoteTotalsNeedNoRawCastGrantBecauseTheImmutableHeaderOwnsTheirCounts()
    {
        await InsertActiveVoteAsync("stored", ["Yea", "Nay", "Present"]);
        await ExecuteAsync("REVOKE SELECT (vote_cast) ON vote_member_list_entries FROM civic_lens_public_owner");

        Assert.Equal(["1 1 1 0"], await PublicAsync("SELECT concat_ws(' ', yea, nay, present, not_voting) FROM public_api.votes WHERE record_id = 'stored'"));
    }

    [Fact]
    public async Task VoteCountsEqualTheTotalsOfTheVoteTallyAndAreNullWhenTheCastsAreCandidateNamesBecauseOneRuleMustGiveBothAnswers()
    {
        string[] plain = ["Yea", "Aye", "Nay", "No", "Present", "Not Voting", "Not Voting"];
        string[] speaker = ["Johnson (LA)", "Emmer", "Jeffries", "Jeffries"];

        var plainTotals = await TallyTotalsAsync(plain, ["Party total D: Yea 2, Nay 2, Present 1, Not voting 2"]);
        var speakerTotals = await TallyTotalsAsync(speaker, ["Candidate total Johnson (LA): 1", "Candidate total Emmer: 1", "Candidate total Jeffries: 2"]);

        Assert.Equal(new PublishedVoteTotals { Yea = 2, Nay = 2, Present = 1, NotVoting = 2 }, plainTotals);
        Assert.Null(speakerTotals);
        Assert.Equal([$"{plainTotals!.Yea} {plainTotals.Nay} {plainTotals.Present} {plainTotals.NotVoting}"], await ViewCountsAsync("plain", plain));
        Assert.Equal(["none none none none"], await ViewCountsAsync("speaker", speaker));
    }

    // The fact rows hold other text than the columns. A view that reads the fact rows again shows the wrong value, or fails because the view owner has no grant on them.
    [Fact]
    public async Task QuestionAndResultComeFromTheRevisionColumnsAndNotFromTheFactRowsBecauseTheColumnsOwnTheTwoValues()
    {
        await InsertActiveVoteAsync("columns", ["Yea"], ["Question: Fact question", "Result: Fact result"], question: "Column question", result: "Column result");
        await InsertActiveVoteAsync("unasked", ["Yea"], question: null, result: "Failed");

        Assert.Equal(["Column question|Column result"], await PublicAsync("SELECT question || '|' || result FROM public_api.votes WHERE record_id = 'columns'"));
        Assert.Equal(["none|Failed"], await PublicAsync("SELECT coalesce(question, 'none') || '|' || result FROM public_api.votes WHERE record_id = 'unasked'"));
    }

    [Fact]
    public async Task AMemberRowCarriesTheOfficialIdAndHidesTheBioguideIdBecausePublicContractsKeyOfficialsByOurId()
    {
        await SeedAsync();

        Assert.Equal(["p0"], await PublicAsync("SELECT official_id FROM public_api.vote_members WHERE record_id = 'c'"));
        await using var connection = await OpenPublicAsync();
        await AssertDeniedAsync(connection, "SELECT bioguide_id FROM public_api.vote_members", "42703");
        Assert.Equal(["p0"], await PublicAsync("SELECT official_id FROM public_api.member_votes WHERE record_id = 'c'"));
        await AssertDeniedAsync(connection, "SELECT bioguide_id FROM public_api.member_votes", "42703");
    }

    [Fact]
    public async Task AnUntrackedIssueNeverAppearsInTheIssueViewBecauseNoPublicViewMayShowIt()
    {
        await SeedAsync();
        await ExecuteAsync("""
            INSERT INTO vote_revision_issues (record_id, revision_number, issue_id, position, is_tracked) VALUES ('d', 1, 'tax', 1, false);
            """);

        Assert.Equal(["health"], await PublicAsync("SELECT issue_id FROM public_api.vote_issues WHERE record_id = 'd'"));
        Assert.Empty(await PublicAsync("SELECT issue_id FROM public_api.vote_issues WHERE issue_id = 'tax'"));
    }

    [Fact]
    public async Task TheIssueIdsOfAMemberVoteAreTheTrackedIssuesOfTheVoteIssueViewBecauseTheIssueFilterMustAgreeWithTheIssueList()
    {
        await InsertActiveVoteAsync("tagged", ["Yea", "Nay"]);
        await InsertActiveVoteAsync("plain", ["Yea"]);
        await ExecuteAsync("""
            INSERT INTO vote_revision_issues (record_id, revision_number, issue_id, position, is_tracked) VALUES
              ('tagged', 1, 'tax', 0, true), ('tagged', 1, 'health', 1, true), ('tagged', 1, 'zoning', 2, false);
            """);

        Assert.Equal(["tagged p0 health,tax", "tagged p1 health,tax", "plain p0 none"],
            await PublicAsync("SELECT record_id || ' ' || official_id || ' ' || coalesce((SELECT string_agg(x, ',' ORDER BY x) FROM unnest(issue_ids) x), 'none') FROM public_api.member_votes ORDER BY record_id DESC, official_id"));
        Assert.Equal(["health,tax"], await PublicAsync("SELECT string_agg(issue_id, ',' ORDER BY issue_id) FROM public_api.vote_issues WHERE record_id = 'tagged'"));
    }

    [Fact]
    public async Task TheGrantsStepRunTwiceChangesNothingBecauseEveryDbMigrateRunsIt()
    {
        var store = PostgresCollectionAttemptStore.FromConnectionString(connectionString);
        await store.MigrateAsync();
        var before = await GrantStateAsync();

        await store.MigrateAsync();

        Assert.Equal(before, await GrantStateAsync());
    }

    [Fact]
    public async Task TheGrantsStepRestoresWhatARestoreWithNoPrivilegesDropsBecauseDbMigrateMustCloseTheGapAfterARestore()
    {
        var store = PostgresCollectionAttemptStore.FromConnectionString(connectionString);
        await store.MigrateAsync();
        var before = await GrantStateAsync();
        await ExecuteAsync($"""
            REVOKE SELECT ON ALL TABLES IN SCHEMA public_api FROM {Role};
            REVOKE USAGE ON SCHEMA public_api FROM {Role};
            REVOKE SELECT ON publication_releases, publication_record_states, vote_revisions, vote_revision_party_tallies, vote_revision_facts, vote_member_list_entries, vote_revision_issues,
                action_revisions, action_revision_sponsorships, action_revision_issues, zcta_districts, roster_snapshots, collection_attempts FROM {Owner};
            ALTER ROLE {Role} RESET ALL;
            ALTER ROLE {Role} CONNECTION LIMIT -1;
            GRANT TEMPORARY ON DATABASE {new NpgsqlConnectionStringBuilder(connectionString).Database} TO PUBLIC;
            GRANT EXECUTE ON FUNCTION pg_advisory_lock(bigint), pg_try_advisory_lock(bigint), pg_try_advisory_xact_lock(bigint) TO PUBLIC;
            """);
        await SeedAsync();
        await using (var broken = await OpenPublicAsync())
            await AssertDeniedAsync(broken, "SELECT count(*) FROM public_api.votes");

        await store.MigrateAsync();

        Assert.Equal(["a", "c", "d"], await PublicAsync("SELECT DISTINCT record_id FROM public_api.votes ORDER BY 1"));
        Assert.Equal(["False"], await QueryAsync("SELECT has_database_privilege('public', current_database(), 'TEMPORARY')", connectionString));
        Assert.Equal(["0"], await QueryAsync("""
            SELECT count(*) FROM pg_proc p WHERE p.pronamespace = 'pg_catalog'::regnamespace AND p.proname LIKE 'pg\_%advisory\_%'
               AND has_function_privilege('public', p.oid, 'EXECUTE')
            """, connectionString));
        Assert.Equal(before, await GrantStateAsync());
        Assert.Equal(["default_transaction_read_only=on", "idle_in_transaction_session_timeout=10s", "idle_session_timeout=60s", "statement_timeout=2s"],
            await QueryAsync($"SELECT unnest(setconfig) FROM pg_db_role_setting WHERE setrole = '{Role}'::regrole ORDER BY 1", connectionString));
        await using var restored = await OpenPublicAsync();
        await AssertDeniedAsync(restored, "SELECT pg_advisory_lock(1)");
        await AssertDeniedAsync(restored, "SELECT * FROM public.vote_revisions");
    }

    [Fact]
    public async Task TheIssueVotesViewShowsTheRowsOfVoteIssuesWithTheirKindBecauseOneRuleMustGiveTheCountsOfBoth()
    {
        await SeedAsync();

        var votes = await PublicAsync("SELECT issue_id || ' ' || kind FROM public_api.issue_votes ORDER BY 1");

        Assert.NotEmpty(votes);
        Assert.Equal(await PublicAsync("""
            SELECT x.issue_id || ' ' || v.kind FROM public_api.vote_issues x
              JOIN public_api.votes v ON v.record_id = x.record_id AND v.revision_number = x.revision_number ORDER BY 1
            """), votes);
    }

    [Fact]
    public async Task TheIssueSponsorsViewShowsTheSponsorsOfBillIssuesBecauseOneRuleMustGiveTheCountsOfBoth()
    {
        await SeedBillsAsync();

        var sponsors = await PublicAsync("SELECT issue_id || ' ' || official_id || ' ' || event_date FROM public_api.issue_sponsors ORDER BY 1");

        Assert.NotEmpty(sponsors);
        Assert.Equal(await PublicAsync("""
            SELECT x.issue_id || ' ' || p.official_id || ' ' || b.event_date FROM public_api.bill_issues x
              JOIN public_api.bills b ON b.record_id = x.record_id AND b.revision_number = x.revision_number
              JOIN public_api.bill_sponsorships p ON p.record_id = x.record_id AND p.revision_number = x.revision_number AND p.role = 'Sponsor' ORDER BY 1
            """), sponsors);
    }

    [Fact]
    public async Task BriefsShowOnlyTheActiveReleaseAndOnlyRevisionsWithTheirColumnsAndOnlyAModelBriefHasTheLabelOfThePublishedWording()
    {
        await ExecuteAsync("""
            SET session_replication_role = replica;
            INSERT INTO publication_releases (release_number, directory_name, actor_subject, published_at_utc_ticks, record_count, is_active)
              VALUES (1, '000001-00000000000000000000000000000001', 'test', 1, 1, true);
            INSERT INTO publication_record_states (kind, record_id, first_release, revision_number, last_release) VALUES
              ('brief', 'A', 1, 1, NULL), ('brief', 'M', 1, 1, NULL), ('brief', 'P', 1, 1, NULL), ('brief', 'O', 1, 1, NULL), ('brief', 'F', 2, 1, NULL), ('vote', 'V', 1, 1, NULL);
            """);
        const string emptyJson = "{}";
        foreach (var (id, origin, columns) in new[] { ("A", "Code", true), ("M", "Model", true), ("P", "Person", true), ("O", "Model", false), ("F", "Model", true) })
            await ExecuteAsync($"""
                SET session_replication_role = replica;
                INSERT INTO brief_revisions (record_id, revision_number, author_subject, author_kind, run_id, revision_json, created_at_utc_ticks,
                    week_start, window_start_utc_ticks, window_end_utc_ticks, first_release_number, cutoff_release_number, fingerprint, headline, text_origin,
                    coverage_considered, coverage_sent_to_model)
                VALUES ('{id}', 1, 'test', {(origin == "Person" ? "'Person', NULL" : $"'Analysis', '{new string(id[0], 32)}'")}, '{emptyJson}', 1,
                    {(columns ? $"'2026-05-04', 0, 6048000000000, 1, 1, '{new string('c', 64)}', 'Headline {id}', '{origin}', 0, 0" : "NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL")});
                """);

        Assert.Equal(
        [
            "A Code none", $"M Model {PublishedWording.ModelBrief}", "P Person none"
        ], await PublicAsync("SELECT record_id || ' ' || text_origin || ' ' || coalesce(label, 'none') FROM public_api.briefs ORDER BY 1"));
        Assert.Equal(["1"], await PublicAsync("SELECT DISTINCT release_number::text FROM public_api.briefs"));
        Assert.Equal(["2026-05-04"], await PublicAsync("SELECT DISTINCT week_start::text FROM public_api.briefs"));
    }

    [Fact]
    public async Task OnlyBillsOfTheActiveReleaseAppearAndAWithdrawnOrFutureOrNonActionRecordNeverDoes()
    {
        await SeedBillsAsync();
        string[] views = ["bills", "bill_sponsorships", "bill_issues"];

        foreach (var view in views)
            Assert.Equal(["A", "C", "M"], await PublicAsync($"SELECT DISTINCT record_id FROM public_api.{view} ORDER BY 1"));

        await ExecuteAsync("UPDATE publication_releases SET is_active = false; UPDATE publication_releases SET is_active = true WHERE release_number = 1");

        foreach (var view in views)
            Assert.Equal(["A", "W"], await PublicAsync($"SELECT DISTINCT record_id FROM public_api.{view} ORDER BY 1"));

        await ExecuteAsync("UPDATE publication_releases SET is_active = false");
        Assert.Empty(await PublicAsync("SELECT record_id FROM public_api.bills"));
    }

    [Fact]
    public async Task ABillKeepsItsBillIdentityAndPolicyAreaForTheRouteFilters()
    {
        await SeedBillsAsync();

        Assert.Equal(["A|119|hr|7|A housing bill|Headline A|Summary A|Health|health|2"],
            await PublicAsync("""
                SELECT concat_ws('|', record_id, congress, bill_type, bill_number, bill_title, headline, summary, policy_area, policy_area_id, release_number)
                  FROM public_api.bills WHERE record_id = 'A'
                """));
    }

    [Fact]
    public async Task ARevisionWithNoWrittenColumnsOrNoDerivedColumnsOrNoSponsorshipAppearsInNoBillViewBecauseItHoldsNothingToShow()
    {
        await SeedBillsAsync();
        await ExecuteAsync($"""
            SET session_replication_role = replica;
            INSERT INTO publication_record_states (kind, record_id, first_release, revision_number, last_release) VALUES
              ('action', 'legacy', 1, 1, NULL), ('action', 'underived', 1, 1, NULL), ('action', 'lonely', 1, 1, NULL);
            INSERT INTO action_revisions (record_id, revision_number, author_subject, author_kind, revision_json, created_at_utc_ticks)
              VALUES ('legacy', 1, 'test', 'Person', 'x', 1);
            {PublicActionRows.ActionSponsorship("legacy", 1, 0, "p1", "Sponsor", true, "2026-01-10")}
            {PublicActionRows.ActionIssue("legacy", 1, "health", 0, true)}
            {PublicActionRows.ActionRevision("underived", 1, "Person", eventOn: null, cosponsorCount: null)}
            {PublicActionRows.ActionSponsorship("underived", 1, 0, "p1", "Sponsor", true, "2026-01-10")}
            {PublicActionRows.ActionIssue("underived", 1, "health", 0, true)}
            {PublicActionRows.ActionRevision("lonely", 1, "Person", "2026-01-10", 0)}
            {PublicActionRows.ActionIssue("lonely", 1, "health", 0, true)}
            """);

        foreach (var view in new[] { "bills", "bill_sponsorships", "bill_issues" })
            Assert.Equal(["A", "C", "M"], await PublicAsync($"SELECT DISTINCT record_id FROM public_api.{view} ORDER BY 1"));
    }

    [Fact]
    public async Task NoBillChildViewShowsARecordThatBillsHidesBecauseARouteJoinsThemByRecordId()
    {
        await SeedBillsAsync();
        await ExecuteAsync($"""
            SET session_replication_role = replica;
            INSERT INTO publication_record_states (kind, record_id, first_release, revision_number, last_release) VALUES
              ('action', 'untracked', 1, 1, NULL), ('action', 'hidden', 1, 1, NULL);
            {PublicActionRows.ActionRevision("untracked", 1, "Person", "2026-02-20", 1)}
            {PublicActionRows.ActionSponsorship("untracked", 1, 0, "p1", "Sponsor", true, "2026-01-10")}
            {PublicActionRows.ActionRevision("hidden", 1, "Person", eventOn: null, cosponsorCount: null)}
            {PublicActionRows.ActionSponsorship("hidden", 1, 0, "p1", "Sponsor", true, "2026-01-10")}
            {PublicActionRows.ActionIssue("hidden", 1, "health", 0, true)}
            """);

        var bills = await PublicAsync("SELECT record_id || ' ' || revision_number FROM public_api.bills ORDER BY 1");
        var sponsorships = await PublicAsync("SELECT DISTINCT record_id || ' ' || revision_number FROM public_api.bill_sponsorships ORDER BY 1");
        var issues = await PublicAsync("SELECT DISTINCT record_id || ' ' || revision_number FROM public_api.bill_issues ORDER BY 1");

        Assert.Equal(["A 1", "C 1", "M 1", "untracked 1"], bills);
        Assert.Empty(sponsorships.Except(bills));
        Assert.Empty(issues.Except(bills));
        Assert.Contains("untracked 1", sponsorships);
        Assert.DoesNotContain("untracked 1", issues);
    }

    [Fact]
    public async Task ABillShowsTheBillLineOfTheEntryRowOfItsActiveRevisionAndNoneWhenTheRowHoldsNoLineOrDoesNotExist()
    {
        string[] ids = [new('a', 64), new('b', 64), new('c', 64)];
        await ExecuteAsync($"""
            SET session_replication_role = replica;
            INSERT INTO publication_releases (release_number, directory_name, actor_subject, published_at_utc_ticks, record_count, is_active)
              VALUES (1, '000001-00000000000000000000000000000001', 'test', 1, 3, true);
            INSERT INTO publication_record_states (kind, record_id, first_release, revision_number, last_release) VALUES
              {string.Join(", ", ids.Select(id => $"('action', '{id}', 1, 1, NULL)"))};
            {string.Join('\n', ids.Select(id => PublicActionRows.ActionRevision(id, 1, "Person", "2026-02-20", 1) + PublicActionRows.ActionSponsorship(id, 1, 0, "p1", "Sponsor", true, "2026-01-10")))}
            INSERT INTO published_entries (kind, record_id, revision_number, action_record_id, action_revision_number, headline, bill_title, bill_line) VALUES
              ('action', '{ids[0]}', 1, '{ids[0]}', 1, 'H', 'T', 'This bill funds rent aid.'),
              ('action', '{ids[0]}', 2, '{ids[0]}', 2, 'H', 'T', 'This line is of another revision.'),
              ('action', '{ids[1]}', 1, '{ids[1]}', 1, 'H', 'T', NULL);
            """);

        Assert.Equal(["a|This bill funds rent aid.", "b|none", "c|none"], await PublicAsync(
            "SELECT left(record_id, 1) || '|' || coalesce(bill_line, 'none') FROM public_api.bills ORDER BY record_id"));
    }

    [Fact]
    public async Task EveryAiOrCodeBillHasItsLabelAndAPersonBillHasNone()
    {
        await SeedBillsAsync();

        Assert.Equal(
        [
            $"A Person none", $"C Code {PublishedRecordLabels.CodeWording}", $"M Model {PublishedRecordLabels.ModelWording}"
        ], await PublicAsync("SELECT record_id || ' ' || text_origin || ' ' || coalesce(label, 'none') FROM public_api.bills ORDER BY 1"));
    }

    [Fact]
    public async Task ABillThatCitesBillTextServesItsTextLabelWhyItMattersAndVersionAndOnlyModelWordingCarriesWhyItMattersBecauseModelTextNeedsItsLabel()
    {
        await SeedBillsAsync();
        await ExecuteAsync("""
            SET session_replication_role = replica;
            INSERT INTO bill_text_versions (congress, bill_type, bill_number, package_id, version_type, version_date, source_url, extraction_id, chars_total, chars_read)
              VALUES (119, 'hr', 7, 'BILLS-119hr7ih', 'Introduced in House', '2025-01-03',
                      'https://www.congress.gov/119/bills/hr7/BILLS-119hr7ih.htm', 'x', 85000, 59987);
            UPDATE action_revisions SET text_package_id = 'BILLS-119hr7ih', why_it_matters = 'It pays landlords.' WHERE record_id IN ('A', 'M');
            """);

        Assert.Equal(
        [
            "A|Person|none|none|Introduced in House|2025-01-03|59987|85000",
            $"C|Code|{PublishedRecordLabels.CodeWording}|none|none|none|none|none",
            $"M|Model|{BillTextWords.Label}|It pays landlords.|Introduced in House|2025-01-03|59987|85000"
        ], await PublicAsync("""
            SELECT concat_ws('|', record_id, text_origin, coalesce(label, 'none'), coalesce(why_it_matters, 'none'), coalesce(text_version_name, 'none'),
                   coalesce(text_version_date::text, 'none'), coalesce(text_chars_read::text, 'none'), coalesce(text_chars_total::text, 'none'))
              FROM public_api.bills ORDER BY record_id
            """));
    }

    [Fact]
    public async Task EventDateAndCosponsorCountEqualTheStoredColumnsBecauseCSharpWritesThemOnceAndTheViewMustNotComputeThem()
    {
        var revision = ActionRevisionFixtures.Model() with
        {
            RecordId = "derived",
            Sponsorships =
            [
                Sponsorship("p1", ActionSponsorshipRole.Sponsor, "2026-01-10", null), Sponsorship("p2", ActionSponsorshipRole.Cosponsor, "2026-01-10", null),
                Sponsorship("p1", ActionSponsorshipRole.Cosponsor, "2026-01-12", null), Sponsorship("p3", ActionSponsorshipRole.Cosponsor, "2026-02-01", "2026-02-20"),
                Sponsorship("p4", ActionSponsorshipRole.Cosponsor, "2026-01-15", null), Sponsorship("p4", ActionSponsorshipRole.Cosponsor, "2026-01-16", null)
            ]
        };
        await InsertActiveBillAsync(revision, "Person");
        await InsertActiveBillAsync(ActionRevisionFixtures.Model() with { RecordId = "stored", Sponsorships = [Sponsorship("p1", ActionSponsorshipRole.Sponsor, "2026-01-10", null)] },
            "Person", eventOn: "2030-12-31", cosponsorCount: 42);

        Assert.Equal(new DateOnly(2026, 2, 20), revision.EventOn());
        Assert.Equal(2, revision.CosponsorCount());
        Assert.Equal(["derived 2026-02-20 2 1"], await PublicAsync("SELECT concat_ws(' ', record_id, event_date, cosponsor_count, sponsor_count) FROM public_api.bills WHERE record_id = 'derived'"));
        Assert.Equal(["stored 2030-12-31 42"], await PublicAsync("SELECT concat_ws(' ', record_id, event_date, cosponsor_count) FROM public_api.bills WHERE record_id = 'stored'"));
    }

    [Fact]
    public async Task ASponsorshipRowKeepsItsOrderOfficialIdRoleAndOriginalFlagAndHidesTheBioguideId()
    {
        await SeedBillsAsync();

        Assert.Equal(
        [
            "0 p1 Sponsor t 2026-01-10 none", "1 p2 Cosponsor t 2026-01-10 none", "2 p3 Cosponsor f 2026-02-01 2026-02-20"
        ], await PublicAsync("""
            SELECT concat_ws(' ', "position", official_id, role, is_original, sponsored_on, coalesce(withdrawn_on::text, 'none'))
              FROM public_api.bill_sponsorships WHERE record_id = 'A' ORDER BY "position"
            """));
        await using var connection = await OpenPublicAsync();
        await AssertDeniedAsync(connection, "SELECT bioguide_id FROM public_api.bill_sponsorships", "42703");
        await AssertDeniedAsync(connection, "SELECT person_id FROM public_api.bill_sponsorships", "42703");
    }

    [Fact]
    public async Task AnUntrackedBillIssueNeverAppearsInTheIssueViewBecauseNoPublicViewMayShowIt()
    {
        await SeedBillsAsync();
        await ExecuteAsync($"""
            SET session_replication_role = replica;
            {PublicActionRows.ActionIssue("A", 1, "tax", 1, false)}
            {PublicActionRows.ActionIssue("A", 1, "labor", 2, true)}
            """);

        Assert.Equal(["health 0", "labor 2"], await PublicAsync("SELECT issue_id || ' ' || \"position\" FROM public_api.bill_issues WHERE record_id = 'A' ORDER BY \"position\""));
        Assert.Empty(await PublicAsync("SELECT issue_id FROM public_api.bill_issues WHERE issue_id = 'tax'"));
        await using var connection = await OpenPublicAsync();
        await AssertDeniedAsync(connection, "SELECT is_tracked FROM public_api.bill_issues", "42703");
    }

    [Fact]
    public async Task TheRoleReadsNoNeverPublicBillFieldBecauseNoViewShowsOneAndNoBaseTableGrantExists()
    {
        await SeedBillsAsync();
        string[] neverPublic =
        [
            "suggestion_headline", "suggestion_summary", "suggestion_run_id", "suggestion_fingerprint", "author_subject", "author_kind", "run_id", "code_version",
            "candidate_id", "fingerprint", "subjects_hash", "issue_revision_id", "issue_candidates_recorded", "source_id", "summary_version", "revision_json",
            "created_at_utc_ticks", "bill_citation_extraction_id", "summary_citation_start", "policy_area_citation_length"
        ];
        await using var connection = await OpenPublicAsync();
        await using (var lift = new NpgsqlCommand("SET default_transaction_read_only = off", connection)) await lift.ExecuteNonQueryAsync();

        foreach (var column in neverPublic)
        {
            await AssertDeniedAsync(connection, $"SELECT {column} FROM public_api.bills", "42703");
            await AssertDeniedAsync(connection, $"SELECT {column} FROM public.action_revisions");
        }
        await AssertDeniedAsync(connection, "SELECT bioguide_id FROM public.action_revision_sponsorships");
        await AssertDeniedAsync(connection, "SELECT record_id FROM public.action_revision_issues");
        await AssertDeniedAsync(connection, "SELECT headline FROM public.action_revisions");
        await AssertDeniedAsync(connection, "UPDATE public.action_revisions SET headline = 'x'");
        await AssertDeniedAsync(connection, "SELECT bill_line FROM public.published_entries");
        await AssertDeniedAsync(connection, "SELECT headline FROM public.published_entries");
        await AssertDeniedAsync(connection, "UPDATE public.published_entries SET bill_line = 'x'");
    }

    [Fact]
    public async Task TheNameViewsShowOnlyTheRowsOfTheActiveReleaseAndFollowARollbackBecauseTheApiMustShowWhatTheActiveReleaseShowed()
    {
        await ExecuteAsync("""
            SET session_replication_role = replica;
            INSERT INTO publication_releases (release_number, directory_name, actor_subject, published_at_utc_ticks, record_count, is_active) VALUES
              (1, '000001-00000000000000000000000000000001', 'test', 1, 0, false),
              (2, '000002-00000000000000000000000000000002', 'test', 2, 0, true);
            INSERT INTO release_officials (release_number, official_id, name) VALUES (1, 'mayor', 'Old Name'), (2, 'mayor', 'New Name');
            INSERT INTO release_official_terms (release_number, official_id, "position", office, kind, start_on) VALUES
              (1, 'mayor', 0, 'Mayor', 'Executive', '2020-01-01'), (2, 'mayor', 0, 'Mayor', 'Executive', '2020-01-01');
            INSERT INTO release_issues (release_number, issue_id, name, definition) VALUES (1, 'housing', 'Housing', 'Old'), (2, 'housing', 'Housing costs', 'New');
            INSERT INTO release_issue_includes (release_number, issue_id, "position", text) VALUES (1, 'housing', 0, 'Old include'), (2, 'housing', 0, 'New include');
            INSERT INTO release_issue_excludes (release_number, issue_id, "position", text) VALUES (1, 'housing', 0, 'Old exclude'), (2, 'housing', 0, 'New exclude');
            INSERT INTO release_policy_areas (release_number, policy_area_id, name) VALUES (1, 'land-use', 'Land'), (2, 'land-use', 'Land use');
            INSERT INTO release_issue_party_lineups (release_number, issue_id, unknown_bills) VALUES (1, 'housing', 1), (2, 'housing', 0);
            INSERT INTO release_issue_party_counts (release_number, issue_id, party, bills) VALUES (1, 'housing', 'Old party', 2), (2, 'housing', 'New party', 3);
            """);

        Assert.Equal(["2 New Name"], await PublicAsync("SELECT release_number || ' ' || name FROM public_api.release_officials"));
        Assert.Equal(["2 Mayor"], await PublicAsync("SELECT release_number || ' ' || office FROM public_api.release_official_terms"));
        Assert.Equal(["2 Housing costs New"], await PublicAsync("SELECT release_number || ' ' || name || ' ' || definition FROM public_api.release_issues"));
        Assert.Equal(["New include"], await PublicAsync("SELECT text FROM public_api.release_issue_includes"));
        Assert.Equal(["New exclude"], await PublicAsync("SELECT text FROM public_api.release_issue_excludes"));
        Assert.Equal(["2 Land use"], await PublicAsync("SELECT release_number || ' ' || name FROM public_api.release_policy_areas"));
        Assert.Equal(["2 housing 0 New party 3"], await PublicAsync("SELECT release_number || ' ' || issue_id || ' ' || unknown_bills || ' ' || party || ' ' || bills FROM public_api.release_issue_party_lineups"));

        await ExecuteAsync("UPDATE publication_releases SET is_active = false; UPDATE publication_releases SET is_active = true WHERE release_number = 1");
        Assert.Equal(["1 Old Name"], await PublicAsync("SELECT release_number || ' ' || name FROM public_api.release_officials"));
        Assert.Equal(["1 housing 1 Old party 2"], await PublicAsync("SELECT release_number || ' ' || issue_id || ' ' || unknown_bills || ' ' || party || ' ' || bills FROM public_api.release_issue_party_lineups"));
        await ExecuteAsync("UPDATE publication_releases SET is_active = false");
        Assert.Empty(await PublicAsync("SELECT official_id FROM public_api.release_officials"));
    }

    private async Task SeedBillsAsync()
    {
        await ExecuteAsync($"""
            SET session_replication_role = replica;
            INSERT INTO publication_releases (release_number, directory_name, actor_subject, published_at_utc_ticks, record_count, is_active) VALUES
              (1, '000001-00000000000000000000000000000001', 'test', 1, 2, false),
              (2, '000002-00000000000000000000000000000002', 'test', 2, 3, true);
            -- A: both releases. M and C: from release 2. W: release 1 only. F: from a later release. K: a vote record with an action row of the same ID.
            INSERT INTO publication_record_states (kind, record_id, first_release, revision_number, last_release) VALUES
              ('action', 'A', 1, 1, NULL), ('action', 'M', 2, 1, NULL), ('action', 'C', 2, 1, NULL), ('action', 'W', 1, 1, 1),
              ('action', 'F', 3, 1, NULL), ('vote', 'K', 1, 1, NULL);
            {string.Join('\n', new[] { ("A", "Person"), ("M", "Model"), ("C", "Code"), ("W", "Person"), ("F", "Person"), ("K", "Person") }
                .Select(bill => BillRows(bill.Item1, bill.Item2)))}
            """);
    }

    private static string BillRows(string record, string origin) => $"""
        {PublicActionRows.ActionRevision(record, 1, origin, "2026-02-20", 1)}
        {PublicActionRows.ActionSponsorship(record, 1, 0, "p1", "Sponsor", true, "2026-01-10")}
        {PublicActionRows.ActionSponsorship(record, 1, 1, "p2", "Cosponsor", true, "2026-01-10")}
        {PublicActionRows.ActionSponsorship(record, 1, 2, "p3", "Cosponsor", false, "2026-02-01", "2026-02-20")}
        {PublicActionRows.ActionIssue(record, 1, "health", 0, true)}
        """;

    private async Task InsertActiveBillAsync(ActionDraftRevision revision, string origin, string? eventOn = null, int? cosponsorCount = null)
    {
        var sponsorships = revision.Sponsorships.Select((item, position) => PublicActionRows.ActionSponsorship(revision.RecordId, 1, position, item.PersonId, item.Role.ToString(),
            item.IsOriginal, item.SponsoredOn.ToString("yyyy-MM-dd"), item.WithdrawnOn?.ToString("yyyy-MM-dd")));
        await ExecuteAsync($"""
            SET session_replication_role = replica;
            INSERT INTO publication_releases (release_number, directory_name, actor_subject, published_at_utc_ticks, record_count, is_active)
              VALUES (1, '000001-00000000000000000000000000000001', 'test', 1, 1, true) ON CONFLICT DO NOTHING;
            INSERT INTO publication_record_states (kind, record_id, first_release, revision_number, last_release) VALUES ('action', '{revision.RecordId}', 1, 1, NULL);
            {PublicActionRows.ActionRevision(revision.RecordId, 1, origin, eventOn ?? revision.EventOn().ToString("yyyy-MM-dd"), cosponsorCount ?? revision.CosponsorCount())}
            {string.Join('\n', sponsorships)}
            """);
    }

    private static ActionSponsorship Sponsorship(string person, ActionSponsorshipRole role, string sponsoredOn, string? withdrawnOn) =>
        new(person, $"B{person}", role, true, DateOnly.Parse(sponsoredOn), withdrawnOn is null ? null : DateOnly.Parse(withdrawnOn), []);

    private sealed class ZipSnapshotSteps(PublicApiViewsTests tests)
    {
        private const string RosterSource = RosterFixtures.SourceId;
        private static readonly DateTimeOffset Morning = new(2026, 11, 3, 6, 0, 0, TimeSpan.Zero);
        private readonly PostgresCollectionAttemptStore attempts = PostgresCollectionAttemptStore.FromConnectionString(tests.connectionString);
        private readonly PostgresMemberRosterStore roster = PostgresMemberRosterStore.FromConnectionString(tests.connectionString);
        private readonly PostgresZctaDistrictStore districts = PostgresZctaDistrictStore.FromConnectionString(tests.connectionString);
        private readonly Dictionary<string, string> rosterAttempts = [];

        public async Task AdmitAsync(char body, int day, int rows) => await AdmitImportedAsync(await ImportOnlyAsync(body, day), rows);

        public async Task<string> ImportOnlyAsync(char body, int day)
        {
            var districtAttempt = await RosterFixtures.ImportCaptureAsync(attempts, new string(body, 64), Morning.AddDays(day), DistrictFixtures.SourceId);
            rosterAttempts[districtAttempt] = await RosterFixtures.ImportCaptureAsync(attempts, new string((char)(body - 'a' + '1'), 64), Morning.AddDays(day + 30), RosterSource);
            return districtAttempt;
        }

        public async Task AdmitImportedAsync(string districtAttempt, int rows)
        {
            await districts.AdmitAsync(districtAttempt, [.. DistrictFixtures.Rows().Take(rows)], default);
            await roster.AdmitAsync(rosterAttempts[districtAttempt], [.. RosterFixtures.Members().Take(rows)], null, default);
        }

        public async Task AssertViewMatchesRosterAsync(int expectedRows)
        {
            Assert.Equal(expectedRows, (await roster.ReadLatestAsync(RosterSource, default)).Count);
            Assert.Equal([expectedRows.ToString()], await tests.PublicAsync("SELECT count(*) FROM public_api.zip_districts"));
        }
    }

    private async Task SeedAsync()
    {
        await ExecuteAsync("""
            SET session_replication_role = replica;
            INSERT INTO publication_releases (release_number, directory_name, actor_subject, published_at_utc_ticks, record_count, is_active) VALUES
              (1, '000001-00000000000000000000000000000001', 'test', 1, 3, false),
              (2, '000002-00000000000000000000000000000002', 'test', 2, 3, true);
            -- a: revision 1 in release 1 only, revision 2 from release 2. b: release 1 only. c: from release 2. d: both.
            INSERT INTO publication_record_states (kind, record_id, first_release, revision_number, last_release) VALUES
              ('vote', 'a', 1, 1, 1), ('vote', 'a', 2, 2, NULL), ('vote', 'b', 1, 1, 1), ('vote', 'c', 2, 1, NULL), ('vote', 'd', 1, 1, NULL),
              ('statement', 'a', 1, 1, NULL);
            """ + string.Join('\n', new[]
        {
            Revision("a", 1, "Person", "1111111111111111111111111111111111111111111111111111111111111111"), Revision("a", 2, "Code", "2222222222222222222222222222222222222222222222222222222222222222"), Revision("b", 1, "Model", "3333333333333333333333333333333333333333333333333333333333333333"),
            Revision("c", 1, "Person", "4444444444444444444444444444444444444444444444444444444444444444"), Revision("d", 1, "Model", "5555555555555555555555555555555555555555555555555555555555555555")
        }) + """
            INSERT INTO vote_member_lists (list_id, member_count, yea_count, nay_count, present_count, not_voting_count, other_count, created_at_utc_ticks) VALUES
              ('1111111111111111111111111111111111111111111111111111111111111111', 1, 1, 0, 0, 0, 0, 1),
              ('2222222222222222222222222222222222222222222222222222222222222222', 7, 2, 2, 1, 2, 0, 1),
              ('3333333333333333333333333333333333333333333333333333333333333333', 1, 0, 1, 0, 0, 0, 1),
              ('4444444444444444444444444444444444444444444444444444444444444444', 1, 0, 0, 1, 0, 0, 1),
              ('5555555555555555555555555555555555555555555555555555555555555555', 1, 0, 0, 0, 1, 0, 1);
            """ + string.Join('\n', new[]
        {
            Entry("1111111111111111111111111111111111111111111111111111111111111111", 0, "Yea"), Entry("3333333333333333333333333333333333333333333333333333333333333333", 0, "Nay"), Entry("4444444444444444444444444444444444444444444444444444444444444444", 0, "Present"),
            Entry("2222222222222222222222222222222222222222222222222222222222222222", 0, "Yea"), Entry("2222222222222222222222222222222222222222222222222222222222222222", 1, "Aye"), Entry("2222222222222222222222222222222222222222222222222222222222222222", 2, "Nay"), Entry("2222222222222222222222222222222222222222222222222222222222222222", 3, "No"), Entry("2222222222222222222222222222222222222222222222222222222222222222", 4, "Present"),
            Entry("2222222222222222222222222222222222222222222222222222222222222222", 5, "Not Voting"), Entry("2222222222222222222222222222222222222222222222222222222222222222", 6, "Not Voting"),
            Entry("5555555555555555555555555555555555555555555555555555555555555555", 0, "Not Voting")
        }) + """
            INSERT INTO vote_revision_issues (record_id, revision_number, issue_id, position, is_tracked) VALUES ('d', 1, 'health', 0, true), ('b', 1, 'tax', 0, false);
            """);
    }

    private static async Task<PublishedVoteTotals?> TallyTotalsAsync(string[] casts, string[] totalLines)
    {
        var scenario = new PublicationScenario();
        var vote = scenario.AddVote('d', totalLines: totalLines, cast: casts[0],
            otherMemberLines: [.. casts[1..].Select((cast, index) => $"Member: X{index}, N, D, MA, {cast}")]);
        return (await PublishedVoteAsync(scenario, vote)).Totals;
    }

    private static async Task<PublishedVote> PublishedVoteAsync(PublicationScenario scenario, string vote)
    {
        var summary = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner,
            PublicationScenario.Request("k", new PublishedRecordReference(PublishedRecordKind.Vote, vote)));
        return JsonSerializer.Deserialize<PublishedVote>(scenario.Releases.Directories[summary.DirectoryName][PublicationProtocol.RecordDataPath(vote)],
            PublicationProtocol.JsonOptions)!;
    }

    private async Task<string[]> ViewCountsAsync(string record, string[] casts)
    {
        await InsertActiveVoteAsync(record, casts);
        return await PublicAsync($"SELECT concat_ws(' ', coalesce(yea::text, 'none'), coalesce(nay::text, 'none'), coalesce(present::text, 'none'), coalesce(not_voting::text, 'none')) FROM public_api.votes WHERE record_id = '{record}'");
    }

    // The fact rows go in last position first, so only the position column can give the order.
    private async Task InsertActiveVoteAsync(string record, string[] casts, string[]? facts = null, string? question = null, string result = "Passed")
    {
        var list = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(record)));
        await ExecuteAsync($"""
            SET session_replication_role = replica;
            INSERT INTO publication_releases (release_number, directory_name, actor_subject, published_at_utc_ticks, record_count, is_active)
              VALUES (1, '000001-00000000000000000000000000000001', 'test', 1, 1, true) ON CONFLICT DO NOTHING;
            INSERT INTO publication_record_states (kind, record_id, first_release, revision_number, last_release) VALUES ('vote', '{record}', 1, 1, NULL);
            INSERT INTO vote_member_lists (list_id, {VoteMemberListSeed.Columns}, created_at_utc_ticks) VALUES ('{list}', {VoteMemberListSeed.Values(casts)}, 1);
            {Revision(record, 1, "Person", list, question, result)}
            {string.Join('\n', casts.Select((cast, index) => Entry(list, index, cast)))}
            {string.Join('\n', (facts ?? []).Select((text, position) => Fact(record, position, text)).Reverse())}
            """);
    }

    private static string Revision(string record, int revision, string origin, string list, string? question = null, string result = "Passed") => $"""
        INSERT INTO vote_revisions (record_id, revision_number, author_subject, author_kind, created_at_utc_ticks,
            headline, summary, text_origin, source_id, vote_source, chamber, congress, session_number, roll_call_number, candidate_id,
            candidate_update_utc_ticks, voted_on, voted_on_citation_extraction_id, voted_on_citation_start, voted_on_citation_length,
            kind, member_list_id, issue_revision_id, issue_candidates_recorded, bill_type, bill_number, question, result)
        VALUES ('{record}', {revision}, 'test', 'Person', 1, 'Headline {record}', 'Summary {record}', '{origin}', 'src', 'CongressGov', 'House', 119, 1, 7,
            'cand', 1, '2026-01-02', 'ext', 0, 1, 'Passage', '{list}', '{new string('a', 64)}', true, 'HR', 1, {(question is null ? "NULL" : $"'{question}'")}, '{result}');
        """;

    private static string Fact(string record, int position, string text) => $"""
        INSERT INTO vote_revision_facts (record_id, revision_number, "position", fact_text, citation_extraction_id, citation_start, citation_length)
        VALUES ('{record}', 1, {position}, '{text.Replace("'", "''")}', 'ext', 0, 1);
        """;

    private static string Entry(string list, int position, string cast) => $"""
        INSERT INTO vote_member_list_entries (list_id, "position", person_id, bioguide_id, vote_cast, citation_extraction_id, citation_start, citation_length)
        VALUES ('{list}', {position}, 'p{position}', 'B{position:000000}', '{cast}', 'ext', 0, 1);
        """;

    private async Task<string[]> GrantStateAsync() => await QueryAsync($"""
        SELECT x FROM (
          SELECT 'rel ' || n.nspname || '.' || c.relname || ' ' || coalesce(c.relacl::text, '') AS x FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
           WHERE n.nspname = 'public_api'
          UNION ALL SELECT 'column ' || c.relname || '.' || a.attname || ' ' || a.attacl::text FROM pg_attribute a JOIN pg_class c ON c.oid = a.attrelid
           WHERE a.attacl IS NOT NULL AND c.relnamespace = 'public'::regnamespace
          UNION ALL SELECT 'schema ' || nspacl::text FROM pg_namespace WHERE nspname = 'public_api'
          UNION ALL SELECT 'database ' || datacl::text FROM pg_database WHERE datname = current_database()
          UNION ALL SELECT 'role ' || rolconnlimit || ' ' || coalesce(rolconfig::text, '') FROM pg_roles
           WHERE rolname = '{Role}'
        ) t ORDER BY x
        """, connectionString);

    private static string ReadOnlyGrantsScript() => string.Join('\n',
        File.ReadAllLines(Path.Combine(RepositoryRoot.FullName, "deploy", "scripts", "readonly-grants.sql")).Where(line => !line.StartsWith('\\')));

    private static async Task AssertDeniedAsync(NpgsqlConnection connection, string sql, string sqlState = "42501")
    {
        await using var command = new NpgsqlCommand(sql, connection);
        var error = await Assert.ThrowsAnyAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        Assert.Equal(sqlState, error.SqlState);
    }

    private static string WritableProbeColumn(string view) =>
        view.StartsWith("issue_", StringComparison.Ordinal) ? "issue_id = 'x'" : view == "zip_districts" ? "zcta = 'x'" : view.StartsWith("official", StringComparison.Ordinal) ? "official_id = 'x'" : view.StartsWith("release_", StringComparison.Ordinal) || view is "party_agreement" or "active_release" ? "release_number = 1" : "record_id = 'x'";

    private async Task<NpgsqlConnection> OpenPublicAsync()
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString) { Username = Role, Password = LoginPassword };
        var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    private async Task<string[]> PublicAsync(string sql)
    {
        await using var connection = await OpenPublicAsync();
        return await ReadAsync(connection, sql);
    }

    private static async Task<string[]> QueryAsync(string sql, string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        return await ReadAsync(connection, sql);
    }

    private static async Task<string[]> ReadAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<string>();
        while (await reader.ReadAsync()) rows.Add(Convert.ToString(reader.GetValue(0))!);
        return [.. rows];
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
