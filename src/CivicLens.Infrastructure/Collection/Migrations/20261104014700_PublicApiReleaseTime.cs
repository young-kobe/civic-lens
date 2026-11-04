using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations
{
    public partial class PublicApiReleaseTime : Migration
    {
        // The new column goes last, so the dependent views keep their definitions. The next db migrate grants the column to the view owner.
        private const string UpSql = """
            CREATE OR REPLACE VIEW public_api.active_records WITH (security_barrier = true) AS
            SELECT s.kind, s.record_id, s.revision_number, r.release_number, r.published_at_utc_ticks
              FROM public.publication_releases r
              JOIN public.publication_record_states s
                ON s.first_release <= r.release_number AND (s.last_release IS NULL OR s.last_release >= r.release_number)
             WHERE r.is_active;
            """;

        // Postgres cannot drop a view column in place, so the old views replace the new ones. The next db migrate grants the role again.
        private const string DownSql = """
            DROP VIEW public_api.active_records CASCADE;

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
                   CASE WHEN t.other_casts = 0 THEN t.yea END AS yea,
                   CASE WHEN t.other_casts = 0 THEN t.nay END AS nay,
                   CASE WHEN t.other_casts = 0 THEN t.present END AS present,
                   CASE WHEN t.other_casts = 0 THEN t.not_voting END AS not_voting,
                   substr(q.fact_text, length('Question: ') + 1) AS question,
                   substr(r.fact_text, length('Result: ') + 1) AS result
              FROM public_api.active_records a
              JOIN public.vote_revisions v ON v.record_id = a.record_id AND v.revision_number = a.revision_number
             CROSS JOIN LATERAL (
                   SELECT count(*) FILTER (WHERE e.vote_cast IN ('Yea', 'Aye')) AS yea,
                          count(*) FILTER (WHERE e.vote_cast IN ('Nay', 'No')) AS nay,
                          count(*) FILTER (WHERE e.vote_cast = 'Present') AS present,
                          count(*) FILTER (WHERE e.vote_cast = 'Not Voting') AS not_voting,
                          count(*) FILTER (WHERE e.vote_cast NOT IN ('Yea', 'Aye', 'Nay', 'No', 'Present', 'Not Voting')) AS other_casts
                     FROM public.vote_member_list_entries e WHERE e.list_id = v.member_list_id) t
              LEFT JOIN LATERAL (
                   SELECT f.fact_text FROM public.vote_revision_facts f
                    WHERE f.record_id = v.record_id AND f.revision_number = v.revision_number AND starts_with(f.fact_text, 'Question: ')
                    ORDER BY f."position" LIMIT 1) q ON true
              LEFT JOIN LATERAL (
                   SELECT f.fact_text FROM public.vote_revision_facts f
                    WHERE f.record_id = v.record_id AND f.revision_number = v.revision_number AND starts_with(f.fact_text, 'Result: ')
                    ORDER BY f."position" LIMIT 1) r ON true
             WHERE a.kind = 'vote' AND v.member_list_id IS NOT NULL;

            CREATE VIEW public_api.vote_members WITH (security_barrier = true) AS
            SELECT a.record_id, a.revision_number, e."position", e.person_id AS official_id, e.vote_cast
              FROM public_api.active_records a
              JOIN public.vote_revisions v ON v.record_id = a.record_id AND v.revision_number = a.revision_number
              JOIN public.vote_member_list_entries e ON e.list_id = v.member_list_id
             WHERE a.kind = 'vote';

            CREATE VIEW public_api.vote_issues WITH (security_barrier = true) AS
            SELECT a.record_id, a.revision_number, i.issue_id
              FROM public_api.active_records a
              JOIN public.vote_revisions v ON v.record_id = a.record_id AND v.revision_number = a.revision_number
              JOIN public.vote_revision_issues i ON i.record_id = v.record_id AND i.revision_number = v.revision_number
             WHERE a.kind = 'vote' AND v.member_list_id IS NOT NULL AND i.is_tracked;

            ALTER VIEW public_api.active_records OWNER TO civic_lens_public_owner;
            ALTER VIEW public_api.votes OWNER TO civic_lens_public_owner;
            ALTER VIEW public_api.vote_members OWNER TO civic_lens_public_owner;
            ALTER VIEW public_api.vote_issues OWNER TO civic_lens_public_owner;
            REVOKE SELECT (published_at_utc_ticks) ON publication_releases FROM civic_lens_public_owner;
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(UpSql);

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(DownSql);
    }
}
