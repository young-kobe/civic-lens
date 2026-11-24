using CivicLens.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations;

[DbContext(typeof(CollectionAttemptDbContext))]
[Migration("20261124120000_WholeVoteTotals")]
public sealed class WholeVoteTotals : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>("totals_written", "vote_revisions", type: "boolean", nullable: false, defaultValue: false);
        migrationBuilder.AddColumn<int>("total_yea", "vote_revisions", type: "integer", nullable: true);
        migrationBuilder.AddColumn<int>("total_nay", "vote_revisions", type: "integer", nullable: true);
        migrationBuilder.AddColumn<int>("total_present", "vote_revisions", type: "integer", nullable: true);
        migrationBuilder.AddColumn<int>("total_not_voting", "vote_revisions", type: "integer", nullable: true);
        migrationBuilder.AddCheckConstraint("ck_vote_revisions_whole_totals", "vote_revisions",
            "(total_yea IS NULL AND total_nay IS NULL AND total_present IS NULL AND total_not_voting IS NULL) OR (totals_written AND total_yea IS NOT NULL AND total_nay IS NOT NULL AND total_present IS NOT NULL AND total_not_voting IS NOT NULL AND total_yea >= 0 AND total_nay >= 0 AND total_present >= 0 AND total_not_voting >= 0 AND total_yea::bigint + total_nay + total_present + total_not_voting <= 600)");
        migrationBuilder.Sql("GRANT SELECT (total_yea, total_nay, total_present, total_not_voting) ON vote_revisions TO civic_lens_public_owner;");
        migrationBuilder.Sql(WholeTotals);
        migrationBuilder.Sql("REVOKE SELECT (list_id, yea_count, nay_count, present_count, not_voting_count, other_count) ON vote_member_lists FROM civic_lens_public_owner;");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("GRANT SELECT (list_id, yea_count, nay_count, present_count, not_voting_count, other_count) ON vote_member_lists TO civic_lens_public_owner;");
        migrationBuilder.Sql(TrackedTotals);
        migrationBuilder.DropCheckConstraint("ck_vote_revisions_whole_totals", "vote_revisions");
        migrationBuilder.DropColumn("totals_written", "vote_revisions");
        migrationBuilder.DropColumn("total_yea", "vote_revisions");
        migrationBuilder.DropColumn("total_nay", "vote_revisions");
        migrationBuilder.DropColumn("total_present", "vote_revisions");
        migrationBuilder.DropColumn("total_not_voting", "vote_revisions");
    }

    private const string WholeTotals = """
        CREATE OR REPLACE VIEW public_api.votes WITH (security_barrier = true) AS
        SELECT a.record_id, a.revision_number, a.release_number,
               v.chamber, v.congress, v.session_number, v.roll_call_number, v.voted_on, v.kind, v.bill_type, v.bill_number,
               v.headline, v.summary, v.text_origin,
               CASE v.text_origin
                 WHEN 'Model' THEN 'An AI summary of the official record'
                 WHEN 'Code' THEN CASE v.chamber WHEN 'Senate' THEN 'Written automatically from Senate roll call data, with no AI' ELSE 'Written automatically from Congress.gov data, with no AI' END
               END AS label,
               v.total_yea::bigint AS yea,
               v.total_nay::bigint AS nay,
               v.total_present::bigint AS present,
               v.total_not_voting::bigint AS not_voting,
               v.question, v.result,
               v.alignment,
               bill.record_id::varchar(64) AS bill_record_id
          FROM public_api.active_records a
          JOIN public.vote_revisions v ON v.record_id = a.record_id AND v.revision_number = a.revision_number
          LEFT JOIN public.release_vote_bills rb ON rb.release_number = a.release_number AND rb.vote_record_id = a.record_id
          LEFT JOIN LATERAL (SELECT b.record_id FROM public_api.bills b WHERE b.record_id = rb.bill_record_id LIMIT 1) bill ON TRUE
         WHERE a.kind = 'vote' AND v.member_list_id IS NOT NULL;
        ALTER VIEW public_api.votes OWNER TO civic_lens_public_owner;
        """;

    private const string TrackedTotals = """
        CREATE OR REPLACE VIEW public_api.votes WITH (security_barrier = true) AS
        SELECT a.record_id, a.revision_number, a.release_number,
               v.chamber, v.congress, v.session_number, v.roll_call_number, v.voted_on, v.kind, v.bill_type, v.bill_number,
               v.headline, v.summary, v.text_origin,
               CASE v.text_origin
                 WHEN 'Model' THEN 'An AI summary of the official record'
                 WHEN 'Code' THEN CASE v.chamber WHEN 'Senate' THEN 'Written automatically from Senate roll call data, with no AI' ELSE 'Written automatically from Congress.gov data, with no AI' END
               END AS label,
               CASE WHEN t.other_count = 0 THEN t.yea_count::bigint END AS yea,
               CASE WHEN t.other_count = 0 THEN t.nay_count::bigint END AS nay,
               CASE WHEN t.other_count = 0 THEN t.present_count::bigint END AS present,
               CASE WHEN t.other_count = 0 THEN t.not_voting_count::bigint END AS not_voting,
               v.question, v.result,
               v.alignment,
               bill.record_id::varchar(64) AS bill_record_id
          FROM public_api.active_records a
          JOIN public.vote_revisions v ON v.record_id = a.record_id AND v.revision_number = a.revision_number
          LEFT JOIN public.release_vote_bills rb ON rb.release_number = a.release_number AND rb.vote_record_id = a.record_id
          LEFT JOIN LATERAL (SELECT b.record_id FROM public_api.bills b WHERE b.record_id = rb.bill_record_id LIMIT 1) bill ON TRUE
          JOIN public.vote_member_lists t ON t.list_id = v.member_list_id
         WHERE a.kind = 'vote' AND v.member_list_id IS NOT NULL;
        ALTER VIEW public_api.votes OWNER TO civic_lens_public_owner;
        """;
}
