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
        ALTER ROLE civic_lens_public SET idle_session_timeout = '60s';

        GRANT SELECT (release_number, published_at_utc_ticks, is_active) ON publication_releases TO civic_lens_public_owner;
        GRANT SELECT (kind, record_id, first_release, revision_number, last_release) ON publication_record_states TO civic_lens_public_owner;
        GRANT SELECT (record_id, revision_number, chamber, congress, session_number, roll_call_number, voted_on, kind,
            bill_type, bill_number, headline, summary, text_origin, member_list_id) ON vote_revisions TO civic_lens_public_owner;
        GRANT SELECT (record_id, revision_number, "position", fact_text) ON vote_revision_facts TO civic_lens_public_owner;
        GRANT SELECT (list_id, "position", person_id, vote_cast) ON vote_member_list_entries TO civic_lens_public_owner;
        GRANT SELECT (record_id, revision_number, issue_id, is_tracked) ON vote_revision_issues TO civic_lens_public_owner;

        GRANT USAGE ON SCHEMA public_api TO civic_lens_public;
        GRANT SELECT ON public_api.active_records, public_api.votes, public_api.vote_members, public_api.vote_issues TO civic_lens_public;
        """;

    public static Task ApplyAsync(DbContext db, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlRawAsync(Sql, cancellationToken);
}
