using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations
{
    /// <summary>
    /// Creates the public_api schema, its two roles, the active release view, and the vote views. The roles
    /// are created without a password and without LOGIN, so the views stay closed until an operator enables the login.
    /// The label wording below repeats PublishedRecordLabels.ModelWording and CodeWording. PublicApiViewsTests compares them.
    /// </summary>
    public partial class PublicApiViews : Migration
    {
        private const string UpSql = """
            DO $$
            BEGIN
              IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'civic_lens_public_owner') THEN
                CREATE ROLE civic_lens_public_owner NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT;
              END IF;
              IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'civic_lens_public') THEN
                CREATE ROLE civic_lens_public NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT;
              END IF;
              EXECUTE format('GRANT CONNECT ON DATABASE %I TO civic_lens_public', current_database());
            END $$;

            ALTER ROLE civic_lens_public CONNECTION LIMIT 5;
            ALTER ROLE civic_lens_public SET default_transaction_read_only = on;
            ALTER ROLE civic_lens_public SET statement_timeout = '2s';
            ALTER ROLE civic_lens_public SET idle_in_transaction_session_timeout = '10s';

            GRANT SELECT (release_number, is_active) ON publication_releases TO civic_lens_public_owner;
            GRANT SELECT (kind, record_id, first_release, revision_number, last_release) ON publication_record_states TO civic_lens_public_owner;
            GRANT SELECT (record_id, revision_number, chamber, congress, session_number, roll_call_number, voted_on, kind,
                bill_type, bill_number, headline, summary, text_origin, member_list_id) ON vote_revisions TO civic_lens_public_owner;
            GRANT SELECT (list_id, "position", bioguide_id, vote_cast) ON vote_member_list_entries TO civic_lens_public_owner;
            GRANT SELECT (record_id, revision_number, issue_id, is_tracked) ON vote_revision_issues TO civic_lens_public_owner;

            CREATE SCHEMA public_api AUTHORIZATION civic_lens_public_owner;

            CREATE VIEW public_api.active_records WITH (security_barrier = true) AS
            SELECT s.kind, s.record_id, s.revision_number, r.release_number
              FROM public.publication_releases r
              JOIN public.publication_record_states s
                ON s.first_release <= r.release_number AND (s.last_release IS NULL OR s.last_release >= r.release_number)
             WHERE r.is_active;

            CREATE VIEW public_api.votes WITH (security_barrier = true) AS
            SELECT a.record_id, a.revision_number, a.release_number,
                   v.chamber, v.congress, v.session_number, v.roll_call_number, v.voted_on, v.kind, v.bill_type, v.bill_number,
                   v.headline, v.summary, v.text_origin,
                   CASE v.text_origin
                     WHEN 'Model' THEN 'An AI summary of the official record'
                     WHEN 'Code' THEN 'Written automatically from Congress.gov data, with no AI'
                   END AS label,
                   count(e.vote_cast) FILTER (WHERE e.vote_cast IN ('Yea', 'Aye')) AS yea,
                   count(e.vote_cast) FILTER (WHERE e.vote_cast IN ('Nay', 'No')) AS nay,
                   count(e.vote_cast) FILTER (WHERE e.vote_cast = 'Present') AS present,
                   count(e.vote_cast) FILTER (WHERE e.vote_cast = 'Not Voting') AS not_voting
              FROM public_api.active_records a
              JOIN public.vote_revisions v ON v.record_id = a.record_id AND v.revision_number = a.revision_number
              LEFT JOIN public.vote_member_list_entries e ON e.list_id = v.member_list_id
             WHERE a.kind = 'vote'
             GROUP BY a.record_id, a.revision_number, a.release_number, v.chamber, v.congress, v.session_number, v.roll_call_number,
                      v.voted_on, v.kind, v.bill_type, v.bill_number, v.headline, v.summary, v.text_origin;

            CREATE VIEW public_api.vote_members WITH (security_barrier = true) AS
            SELECT a.record_id, a.revision_number, e."position", e.bioguide_id, e.vote_cast
              FROM public_api.active_records a
              JOIN public.vote_revisions v ON v.record_id = a.record_id AND v.revision_number = a.revision_number
              JOIN public.vote_member_list_entries e ON e.list_id = v.member_list_id
             WHERE a.kind = 'vote';

            CREATE VIEW public_api.vote_issues WITH (security_barrier = true) AS
            SELECT a.record_id, a.revision_number, i.issue_id, i.is_tracked
              FROM public_api.active_records a
              JOIN public.vote_revision_issues i ON i.record_id = a.record_id AND i.revision_number = a.revision_number
             WHERE a.kind = 'vote';

            ALTER VIEW public_api.active_records OWNER TO civic_lens_public_owner;
            ALTER VIEW public_api.votes OWNER TO civic_lens_public_owner;
            ALTER VIEW public_api.vote_members OWNER TO civic_lens_public_owner;
            ALTER VIEW public_api.vote_issues OWNER TO civic_lens_public_owner;

            GRANT USAGE ON SCHEMA public_api TO civic_lens_public;
            GRANT SELECT ON public_api.active_records, public_api.votes, public_api.vote_members, public_api.vote_issues TO civic_lens_public;
            """;

        // The roles stay, because a role belongs to the cluster and other databases can use it.
        private const string DownSql = """
            DROP SCHEMA public_api CASCADE;
            REVOKE SELECT ON publication_releases, publication_record_states, vote_revisions, vote_member_list_entries,
                vote_revision_issues FROM civic_lens_public_owner;
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(UpSql);

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(DownSql);
    }
}
