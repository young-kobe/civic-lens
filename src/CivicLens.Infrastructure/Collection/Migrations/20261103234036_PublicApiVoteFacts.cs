using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations
{
    public partial class PublicApiVoteFacts : Migration
    {
        // The prefixes repeat VoteFactPrefixes.Question and VoteFactPrefixes.Result. PublicApiViewsTests compares the values with the C# owners.
        private const string UpSql = """
            CREATE OR REPLACE VIEW public_api.votes WITH (security_barrier = true) AS
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
            """;

        // Postgres cannot drop a view column in place, so the old definition replaces the view. The next db migrate grants the role again.
        private const string DownSql = """
            DROP VIEW public_api.votes;

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

            ALTER VIEW public_api.votes OWNER TO civic_lens_public_owner;
            REVOKE SELECT ON vote_revision_facts FROM civic_lens_public_owner;
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(UpSql);

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(DownSql);
    }
}
