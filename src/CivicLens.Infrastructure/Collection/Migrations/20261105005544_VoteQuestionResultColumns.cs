using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations
{
    /// <inheritdoc />
    public partial class VoteQuestionResultColumns : Migration
    {
        // The prefixes repeat VoteFactPrefixes.Question and VoteFactPrefixes.Result. This is the last step that reads them from the fact rows.
        private const string CheckSql = """
            DO $check$
            DECLARE bad text;
            BEGIN
                SELECT r.record_id || '/' || r.revision_number INTO bad FROM vote_revisions r
                WHERE (SELECT count(*) FROM vote_revision_facts f
                        WHERE f.record_id = r.record_id AND f.revision_number = r.revision_number AND starts_with(f.fact_text, 'Result: ')) <> 1
                ORDER BY r.record_id, r.revision_number LIMIT 1;
                IF bad IS NOT NULL THEN
                    RAISE EXCEPTION 'Cannot fill result: vote revision % needs exactly one Result fact. Run votes revisions backfill and votes revisions verify with the previous build first', bad;
                END IF;
            END
            $check$
            """;

        private const string FillSql = """
            UPDATE vote_revisions r SET
                question = (SELECT substr(f.fact_text, length('Question: ') + 1) FROM vote_revision_facts f
                             WHERE f.record_id = r.record_id AND f.revision_number = r.revision_number AND starts_with(f.fact_text, 'Question: ')
                             ORDER BY f."position" LIMIT 1),
                result = (SELECT substr(f.fact_text, length('Result: ') + 1) FROM vote_revision_facts f
                           WHERE f.record_id = r.record_id AND f.revision_number = r.revision_number AND starts_with(f.fact_text, 'Result: ')
                           ORDER BY f."position" LIMIT 1)
            """;

        // Postgres cannot change the type of a view column in place, so the new view replaces the old one. The next db migrate grants the role again.
        private const string UpViewSql = """
            DROP VIEW public_api.votes;

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
                   CASE WHEN t.other_casts = 0 THEN t.not_voting END AS not_voting,
                   v.question,
                   v.result
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

        // The old view reads the fact rows again, so the view owner gets that column grant back.
        private const string DownViewSql = """
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

            ALTER VIEW public_api.votes OWNER TO civic_lens_public_owner;
            GRANT SELECT (record_id, revision_number, "position", fact_text) ON vote_revision_facts TO civic_lens_public_owner;
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("LOCK TABLE vote_revisions, vote_revision_facts IN ACCESS EXCLUSIVE MODE");
            migrationBuilder.Sql(CheckSql);

            migrationBuilder.AddColumn<string>(
                name: "question",
                table: "vote_revisions",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "result",
                table: "vote_revisions",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql(FillSql);
            migrationBuilder.Sql("ALTER TABLE vote_revisions ALTER COLUMN result DROP DEFAULT");
            migrationBuilder.Sql(UpViewSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(DownViewSql);

            migrationBuilder.DropColumn(
                name: "question",
                table: "vote_revisions");

            migrationBuilder.DropColumn(
                name: "result",
                table: "vote_revisions");
        }
    }
}
