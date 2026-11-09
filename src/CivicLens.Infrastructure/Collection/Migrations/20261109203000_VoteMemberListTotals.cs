using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations;

public partial class VoteMemberListTotals : Migration
{
    private const string UpSql = """
        ALTER TABLE vote_member_lists ADD COLUMN yea_count smallint NULL;
        ALTER TABLE vote_member_lists ADD COLUMN nay_count smallint NULL;
        ALTER TABLE vote_member_lists ADD COLUMN present_count smallint NULL;
        ALTER TABLE vote_member_lists ADD COLUMN not_voting_count smallint NULL;
        ALTER TABLE vote_member_lists ADD COLUMN other_count smallint NULL;

        WITH totals AS (
            SELECT l.list_id,
                   count(e.list_id) FILTER (WHERE e.vote_cast IN ('Yea', 'Aye'))::smallint AS yea,
                   count(e.list_id) FILTER (WHERE e.vote_cast IN ('Nay', 'No'))::smallint AS nay,
                   count(e.list_id) FILTER (WHERE e.vote_cast = 'Present')::smallint AS present,
                   count(e.list_id) FILTER (WHERE e.vote_cast = 'Not Voting')::smallint AS not_voting,
                   count(e.list_id) FILTER (WHERE e.vote_cast NOT IN ('Yea', 'Aye', 'Nay', 'No', 'Present', 'Not Voting'))::smallint AS other
              FROM vote_member_lists l LEFT JOIN vote_member_list_entries e ON e.list_id = l.list_id
             GROUP BY l.list_id
        )
        UPDATE vote_member_lists l
           SET yea_count = t.yea, nay_count = t.nay, present_count = t.present,
               not_voting_count = t.not_voting, other_count = t.other
          FROM totals t WHERE t.list_id = l.list_id;

        ALTER TABLE vote_member_lists ALTER COLUMN yea_count SET NOT NULL;
        ALTER TABLE vote_member_lists ALTER COLUMN nay_count SET NOT NULL;
        ALTER TABLE vote_member_lists ALTER COLUMN present_count SET NOT NULL;
        ALTER TABLE vote_member_lists ALTER COLUMN not_voting_count SET NOT NULL;
        ALTER TABLE vote_member_lists ALTER COLUMN other_count SET NOT NULL;

        ALTER TABLE vote_member_lists ADD CONSTRAINT ck_vote_member_lists_totals CHECK (
            yea_count >= 0 AND nay_count >= 0 AND present_count >= 0 AND not_voting_count >= 0 AND other_count >= 0
            AND (yea_count::integer + nay_count + present_count + not_voting_count + other_count) = member_count);
        GRANT SELECT (list_id, yea_count, nay_count, present_count, not_voting_count, other_count)
            ON vote_member_lists TO civic_lens_public_owner;

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

    private const string DownSql = """
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
         CROSS JOIN LATERAL (
               SELECT count(*) FILTER (WHERE e.vote_cast IN ('Yea', 'Aye')) AS yea,
                      count(*) FILTER (WHERE e.vote_cast IN ('Nay', 'No')) AS nay,
                      count(*) FILTER (WHERE e.vote_cast = 'Present') AS present,
                      count(*) FILTER (WHERE e.vote_cast = 'Not Voting') AS not_voting,
                      count(*) FILTER (WHERE e.vote_cast NOT IN ('Yea', 'Aye', 'Nay', 'No', 'Present', 'Not Voting')) AS other_casts
                 FROM public.vote_member_list_entries e WHERE e.list_id = v.member_list_id) t
         WHERE a.kind = 'vote' AND v.member_list_id IS NOT NULL;
        ALTER VIEW public_api.votes OWNER TO civic_lens_public_owner;
        REVOKE SELECT (list_id, yea_count, nay_count, present_count, not_voting_count, other_count)
            ON vote_member_lists FROM civic_lens_public_owner;
        ALTER TABLE vote_member_lists DROP CONSTRAINT ck_vote_member_lists_totals;
        ALTER TABLE vote_member_lists DROP COLUMN yea_count, DROP COLUMN nay_count, DROP COLUMN present_count,
            DROP COLUMN not_voting_count, DROP COLUMN other_count;
        """;

    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(UpSql);
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(DownSql);
}
