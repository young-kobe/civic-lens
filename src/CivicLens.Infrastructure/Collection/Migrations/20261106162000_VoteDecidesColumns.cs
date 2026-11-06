using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations
{
    /// <inheritdoc />
    public partial class VoteDecidesColumns : Migration
    {
        // The view takes the three new columns last, so the view is replaced in place and keeps its owner and grants. The label is a stored column that
        // VoteRevisionRows fills from VoteDecides.Label, so no SQL repeats the wording. The next db migrate grants the view owner the new columns.
        private const string UpViewSql = """
            CREATE OR REPLACE VIEW public_api.votes WITH (security_barrier = true) AS
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
                   v.question,
                   v.result,
                   v.decides_summary, v.decides_text_origin, v.decides_label
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
            """;

        // Postgres cannot drop a column of a view in place, so the old view returns by a drop. The role grants come back at the next db migrate.
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
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "decides_label",
                table: "vote_revisions",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "decides_quote",
                table: "vote_revisions",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "decides_source_hash",
                table: "vote_revisions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "decides_summary",
                table: "vote_revisions",
                type: "character varying(600)",
                maxLength: 600,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "decides_text_origin",
                table: "vote_revisions",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_vote_revisions_decides",
                table: "vote_revisions",
                sql: "(decides_summary IS NULL) = (decides_quote IS NULL) AND (decides_summary IS NULL) = (decides_text_origin IS NULL) AND (decides_summary IS NULL) = (decides_label IS NULL) AND (decides_summary IS NULL) = (decides_source_hash IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_vote_revisions_decides_bill",
                table: "vote_revisions",
                sql: "decides_summary IS NULL OR bill_type IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_vote_revisions_decides_hash",
                table: "vote_revisions",
                sql: "decides_source_hash ~ '^[0-9a-f]{64}$'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_vote_revisions_decides_origin",
                table: "vote_revisions",
                sql: "decides_text_origin = 'Model'");

            migrationBuilder.Sql(UpViewSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(DownViewSql);

            migrationBuilder.DropCheckConstraint(
                name: "ck_vote_revisions_decides",
                table: "vote_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_vote_revisions_decides_bill",
                table: "vote_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_vote_revisions_decides_hash",
                table: "vote_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_vote_revisions_decides_origin",
                table: "vote_revisions");

            migrationBuilder.DropColumn(
                name: "decides_label",
                table: "vote_revisions");

            migrationBuilder.DropColumn(
                name: "decides_quote",
                table: "vote_revisions");

            migrationBuilder.DropColumn(
                name: "decides_source_hash",
                table: "vote_revisions");

            migrationBuilder.DropColumn(
                name: "decides_summary",
                table: "vote_revisions");

            migrationBuilder.DropColumn(
                name: "decides_text_origin",
                table: "vote_revisions");
        }
    }
}
