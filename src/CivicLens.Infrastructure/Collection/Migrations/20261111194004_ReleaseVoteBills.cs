using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations
{
    /// <inheritdoc />
    public partial class ReleaseVoteBills : Migration
    {
        // The view reads the one row that the release pinned for the vote, so it no longer groups public_api.bills for each list read.
        // The commit writes the row with ReleaseBillLines, which links the first action record of the bill that the release holds. That is the record that min(record_id) gave.
        private const string UpVotesSql = """
            CREATE OR REPLACE VIEW public_api.votes WITH (security_barrier = true) AS
            SELECT a.record_id, a.revision_number, a.release_number,
                   v.chamber, v.congress, v.session_number, v.roll_call_number, v.voted_on, v.kind, v.bill_type, v.bill_number,
                   v.headline, v.summary, v.text_origin,
                   CASE v.text_origin
                     WHEN 'Model' THEN 'An AI summary of the official record'
                     WHEN 'Code' THEN 'Written automatically from Congress.gov data, with no AI'
                   END AS label,
                   CASE WHEN t.other_count = 0 THEN t.yea_count::bigint END AS yea,
                   CASE WHEN t.other_count = 0 THEN t.nay_count::bigint END AS nay,
                   CASE WHEN t.other_count = 0 THEN t.present_count::bigint END AS present,
                   CASE WHEN t.other_count = 0 THEN t.not_voting_count::bigint END AS not_voting,
                   v.question, v.result,
                   v.alignment,
                   rb.bill_record_id
              FROM public_api.active_records a
              JOIN public.vote_revisions v ON v.record_id = a.record_id AND v.revision_number = a.revision_number
              LEFT JOIN public.release_vote_bills rb ON rb.release_number = a.release_number AND rb.vote_record_id = a.record_id
              JOIN public.vote_member_lists t ON t.list_id = v.member_list_id
             WHERE a.kind = 'vote' AND v.member_list_id IS NOT NULL;
            ALTER VIEW public_api.votes OWNER TO civic_lens_public_owner;
            """;

        private const string DownVotesSql = """
            CREATE OR REPLACE VIEW public_api.votes WITH (security_barrier = true) AS
            SELECT a.record_id, a.revision_number, a.release_number,
                   v.chamber, v.congress, v.session_number, v.roll_call_number, v.voted_on, v.kind, v.bill_type, v.bill_number,
                   v.headline, v.summary, v.text_origin,
                   CASE v.text_origin
                     WHEN 'Model' THEN 'An AI summary of the official record'
                     WHEN 'Code' THEN 'Written automatically from Congress.gov data, with no AI'
                   END AS label,
                   CASE WHEN t.other_count = 0 THEN t.yea_count::bigint END AS yea,
                   CASE WHEN t.other_count = 0 THEN t.nay_count::bigint END AS nay,
                   CASE WHEN t.other_count = 0 THEN t.present_count::bigint END AS present,
                   CASE WHEN t.other_count = 0 THEN t.not_voting_count::bigint END AS not_voting,
                   v.question, v.result,
                   v.alignment,
                   bill.record_id AS bill_record_id
              FROM public_api.active_records a
              JOIN public.vote_revisions v ON v.record_id = a.record_id AND v.revision_number = a.revision_number
              LEFT JOIN (
                   SELECT b.congress, b.bill_type, b.bill_number, min(b.record_id)::varchar(64) AS record_id
                     FROM public_api.bills b
                    GROUP BY b.congress, b.bill_type, b.bill_number
              ) bill ON bill.congress = v.congress AND bill.bill_type = v.bill_type AND bill.bill_number = v.bill_number
              JOIN public.vote_member_lists t ON t.list_id = v.member_list_id
             WHERE a.kind = 'vote' AND v.member_list_id IS NOT NULL;
            ALTER VIEW public_api.votes OWNER TO civic_lens_public_owner;
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "release_vote_bills",
                columns: table => new
                {
                    release_number = table.Column<int>(type: "integer", nullable: false),
                    vote_record_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    bill_record_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_release_vote_bills", x => new { x.release_number, x.vote_record_id });
                    table.CheckConstraint("ck_release_vote_bills_bill_record_id", "bill_record_id ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("ck_release_vote_bills_vote_record_id", "vote_record_id ~ '^[0-9a-f]{64}$'");
                    table.ForeignKey(
                        name: "FK_release_vote_bills_publication_releases_release_number",
                        column: x => x.release_number,
                        principalTable: "publication_releases",
                        principalColumn: "release_number",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.Sql(UpVotesSql);
            migrationBuilder.Sql("GRANT SELECT (release_number, vote_record_id, bill_record_id) ON release_vote_bills TO civic_lens_public_owner;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(DownVotesSql);
            migrationBuilder.DropTable(
                name: "release_vote_bills");
        }
    }
}
