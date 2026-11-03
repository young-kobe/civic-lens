using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations
{
    public partial class PublicApiViews : Migration
    {
        private const string UpSql = """
            DO $$
            BEGIN
              IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'civic_lens_public_owner') THEN
                CREATE ROLE civic_lens_public_owner NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT;
              END IF;
            END $$;

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
                   -- The wording repeats PublishedRecordLabels.ModelWording and CodeWording. PublicApiViewsTests compares the labels.
                   CASE v.text_origin
                     WHEN 'Model' THEN 'An AI summary of the official record'
                     WHEN 'Code' THEN 'Written automatically from Congress.gov data, with no AI'
                   END AS label,
                   CASE WHEN t.other_casts = 0 THEN t.yea END AS yea,
                   CASE WHEN t.other_casts = 0 THEN t.nay END AS nay,
                   CASE WHEN t.other_casts = 0 THEN t.present END AS present,
                   CASE WHEN t.other_casts = 0 THEN t.not_voting END AS not_voting
              FROM public_api.active_records a
              JOIN public.vote_revisions v ON v.record_id = a.record_id AND v.revision_number = a.revision_number
             CROSS JOIN LATERAL (
                   SELECT count(*) FILTER (WHERE e.vote_cast IN ('Yea', 'Aye')) AS yea,
                          count(*) FILTER (WHERE e.vote_cast IN ('Nay', 'No')) AS nay,
                          count(*) FILTER (WHERE e.vote_cast = 'Present') AS present,
                          count(*) FILTER (WHERE e.vote_cast = 'Not Voting') AS not_voting,
                          count(*) FILTER (WHERE e.vote_cast NOT IN ('Yea', 'Aye', 'Nay', 'No', 'Present', 'Not Voting')) AS other_casts
                     FROM public.vote_member_list_entries e WHERE e.list_id = v.member_list_id) t
             WHERE a.kind = 'vote' AND v.member_list_id IS NOT NULL;

            CREATE VIEW public_api.vote_members WITH (security_barrier = true) AS
            SELECT a.record_id, a.revision_number, e."position", e.bioguide_id, e.vote_cast
              FROM public_api.active_records a
              JOIN public.vote_revisions v ON v.record_id = a.record_id AND v.revision_number = a.revision_number
              JOIN public.vote_member_list_entries e ON e.list_id = v.member_list_id
             WHERE a.kind = 'vote';

            CREATE VIEW public_api.vote_issues WITH (security_barrier = true) AS
            SELECT a.record_id, a.revision_number, i.issue_id, i.is_tracked
              FROM public_api.active_records a
              JOIN public.vote_revisions v ON v.record_id = a.record_id AND v.revision_number = a.revision_number
              JOIN public.vote_revision_issues i ON i.record_id = v.record_id AND i.revision_number = v.revision_number
             WHERE a.kind = 'vote' AND v.member_list_id IS NOT NULL;

            ALTER VIEW public_api.active_records OWNER TO civic_lens_public_owner;
            ALTER VIEW public_api.votes OWNER TO civic_lens_public_owner;
            ALTER VIEW public_api.vote_members OWNER TO civic_lens_public_owner;
            ALTER VIEW public_api.vote_issues OWNER TO civic_lens_public_owner;
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
