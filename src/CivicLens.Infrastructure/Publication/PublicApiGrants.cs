using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.Publication;

internal static class PublicApiGrants
{
    private const string Sql = """
        DO $$
        DECLARE
          lock_function regprocedure;
        BEGIN
          IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'civic_lens_public') THEN
            CREATE ROLE civic_lens_public NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT;
          END IF;
          EXECUTE format('GRANT CONNECT ON DATABASE %I TO civic_lens_public', current_database());
          EXECUTE format('REVOKE TEMPORARY ON DATABASE %I FROM PUBLIC', current_database());
          FOR lock_function IN SELECT p.oid::regprocedure FROM pg_proc p
                                WHERE p.pronamespace = 'pg_catalog'::regnamespace AND p.proname LIKE 'pg\_%advisory\_%'
          LOOP
            EXECUTE format('REVOKE EXECUTE ON FUNCTION %s FROM PUBLIC', lock_function);
          END LOOP;
        END $$;

        ALTER ROLE civic_lens_public CONNECTION LIMIT 5;
        ALTER ROLE civic_lens_public SET default_transaction_read_only = on;
        ALTER ROLE civic_lens_public SET statement_timeout = '2s';
        ALTER ROLE civic_lens_public SET idle_in_transaction_session_timeout = '10s';
        -- Keep this above PublicApiOptions.PublicViewsIdleLifetime plus the 10 second pruning interval of the pool of the API.
        ALTER ROLE civic_lens_public SET idle_session_timeout = '60s';

        GRANT SELECT (release_number, published_at_utc_ticks, is_active) ON publication_releases TO civic_lens_public_owner;
        GRANT SELECT (kind, record_id, first_release, revision_number, last_release) ON publication_record_states TO civic_lens_public_owner;
        GRANT SELECT (record_id, revision_number, chamber, congress, session_number, roll_call_number, voted_on, kind,
            bill_type, bill_number, headline, summary, text_origin, member_list_id, question, result) ON vote_revisions TO civic_lens_public_owner;
        GRANT SELECT (kind, record_id, revision_number, document_type) ON published_entries TO civic_lens_public_owner;
        GRANT SELECT (kind, record_id, revision_number, official_id) ON published_entry_officials TO civic_lens_public_owner;
        GRANT SELECT (list_id, "position", person_id, vote_cast) ON vote_member_list_entries TO civic_lens_public_owner;
        GRANT SELECT (record_id, revision_number, issue_id, is_tracked) ON vote_revision_issues TO civic_lens_public_owner;
        GRANT SELECT (record_id, revision_number, congress, bill_type, bill_number, bill_title, headline, summary, text_origin,
            policy_area, policy_area_id, event_on, cosponsor_count, bill_line) ON action_revisions TO civic_lens_public_owner;
        GRANT SELECT (record_id, revision_number, "position", person_id, role, is_original, sponsored_on, withdrawn_on)
            ON action_revision_sponsorships TO civic_lens_public_owner;
        GRANT SELECT (record_id, revision_number, issue_id, "position", is_tracked) ON action_revision_issues TO civic_lens_public_owner;
        GRANT SELECT (zcta, state, district, land_area_square_meters, attempt_id) ON zcta_districts TO civic_lens_public_owner;
        GRANT SELECT (release_number, official_id, name, wikidata_id, website) ON release_officials TO civic_lens_public_owner;
        GRANT SELECT (release_number, official_id, "position", office, kind, state, district, party, caucus, start_on, end_on)
            ON release_official_terms TO civic_lens_public_owner;
        GRANT SELECT (release_number, issue_id, name, definition) ON release_issues TO civic_lens_public_owner;
        GRANT SELECT (release_number, issue_id, "position", text) ON release_issue_includes TO civic_lens_public_owner;
        GRANT SELECT (release_number, issue_id, "position", text) ON release_issue_excludes TO civic_lens_public_owner;
        GRANT SELECT (release_number, policy_area_id, name) ON release_policy_areas TO civic_lens_public_owner;
        GRANT SELECT (attempt_id) ON roster_snapshots TO civic_lens_public_owner;
        GRANT SELECT (attempt_id, source_id, capture_sha256, observed_at_utc_ticks) ON collection_attempts TO civic_lens_public_owner;

        GRANT USAGE ON SCHEMA public_api TO civic_lens_public;
        GRANT SELECT ON public_api.active_records, public_api.votes, public_api.vote_members, public_api.member_votes, public_api.vote_issues,
            public_api.bills, public_api.bill_sponsorships, public_api.bill_issues, public_api.zip_districts,
            public_api.release_officials, public_api.release_official_terms, public_api.release_issues, public_api.release_issue_includes,
            public_api.release_issue_excludes, public_api.release_policy_areas, public_api.officials, public_api.official_attendance,
            public_api.official_bill_counts, public_api.official_issue_counts, public_api.official_executive_actions TO civic_lens_public;
        """;

    public static Task ApplyAsync(DbContext db, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlRawAsync(Sql, cancellationToken);
}
