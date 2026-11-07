using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations;

public partial class PublicApiBillLookup : Migration
{
    private const string UpSql = """
        CREATE OR REPLACE VIEW public_api.bills WITH (security_barrier = true) AS
        SELECT a.record_id, a.revision_number, a.release_number,
               r.congress, r.bill_type, r.bill_number, r.bill_title, r.headline, r.summary, r.text_origin,
               CASE
                 WHEN r.text_origin = 'Model' AND r.text_package_id IS NOT NULL THEN 'AI summary of the bill text. An AI model wrote this. Each sentence comes with an exact quote from the text. Code checked the quotes, not the meaning of the sentences.'
                 WHEN r.text_origin = 'Model' THEN 'An AI summary of the official record'
                 WHEN r.text_origin = 'Code' THEN 'Written automatically from Congress.gov data, with no AI'
               END AS label,
               r.policy_area, r.policy_area_id, r.event_on AS event_date, s.sponsor_count, r.cosponsor_count, e.bill_line,
               CASE WHEN r.text_origin = 'Model' THEN r.why_it_matters END AS why_it_matters,
               t.version_type AS text_version_name, t.version_date AS text_version_date, t.chars_read AS text_chars_read, t.chars_total AS text_chars_total,
               h.public_law, h.observed_at_utc_ticks AS history_observed_at_utc_ticks, h.action_count AS history_action_count
          FROM public_api.active_records a
          JOIN public.action_revisions r ON r.record_id = a.record_id AND r.revision_number = a.revision_number
         CROSS JOIN LATERAL (
               SELECT count(*) FILTER (WHERE p.role = 'Sponsor') AS sponsor_count
                 FROM public.action_revision_sponsorships p
                WHERE p.record_id = r.record_id AND p.revision_number = r.revision_number) s
          LEFT JOIN public.published_entries e ON e.kind = 'action' AND e.record_id = a.record_id AND e.revision_number = a.revision_number
          LEFT JOIN public.bill_text_versions t ON t.congress = r.congress AND t.bill_type = r.bill_type AND t.bill_number = r.bill_number
                                               AND t.package_id = r.text_package_id

          LEFT JOIN public.release_bill_histories rh ON rh.release_number = a.release_number AND rh.record_id = a.record_id
          LEFT JOIN public.published_bill_histories h ON h.detail_job_id = rh.detail_job_id
         WHERE a.kind = 'action' AND r.headline IS NOT NULL AND r.event_on IS NOT NULL
           AND EXISTS (SELECT 1 FROM public.action_revision_sponsorships eligible
                        WHERE eligible.record_id = r.record_id AND eligible.revision_number = r.revision_number);

        ALTER VIEW public_api.bills OWNER TO civic_lens_public_owner;

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
        """;

    private const string DownSql = """
        CREATE OR REPLACE VIEW public_api.bills WITH (security_barrier = true) AS
        SELECT a.record_id, a.revision_number, a.release_number,
               r.congress, r.bill_type, r.bill_number, r.bill_title, r.headline, r.summary, r.text_origin,
               CASE
                 WHEN r.text_origin = 'Model' AND r.text_package_id IS NOT NULL THEN 'AI summary of the bill text. An AI model wrote this. Each sentence comes with an exact quote from the text. Code checked the quotes, not the meaning of the sentences.'
                 WHEN r.text_origin = 'Model' THEN 'An AI summary of the official record'
                 WHEN r.text_origin = 'Code' THEN 'Written automatically from Congress.gov data, with no AI'
               END AS label,
               r.policy_area, r.policy_area_id, r.event_on AS event_date, s.sponsor_count, r.cosponsor_count, e.bill_line,
               CASE WHEN r.text_origin = 'Model' THEN r.why_it_matters END AS why_it_matters,
               t.version_type AS text_version_name, t.version_date AS text_version_date, t.chars_read AS text_chars_read, t.chars_total AS text_chars_total,
               h.public_law, h.observed_at_utc_ticks AS history_observed_at_utc_ticks, h.action_count AS history_action_count
          FROM public_api.active_records a
          JOIN public.action_revisions r ON r.record_id = a.record_id AND r.revision_number = a.revision_number
         CROSS JOIN LATERAL (
               SELECT count(*) AS total, count(*) FILTER (WHERE p.role = 'Sponsor') AS sponsor_count
                 FROM public.action_revision_sponsorships p
                WHERE p.record_id = r.record_id AND p.revision_number = r.revision_number) s
          LEFT JOIN public.published_entries e ON e.kind = 'action' AND e.record_id = a.record_id AND e.revision_number = a.revision_number
          LEFT JOIN public.bill_text_versions t ON t.congress = r.congress AND t.bill_type = r.bill_type AND t.bill_number = r.bill_number
                                               AND t.package_id = r.text_package_id

          LEFT JOIN public.release_bill_histories rh ON rh.release_number = a.release_number AND rh.record_id = a.record_id
          LEFT JOIN public.published_bill_histories h ON h.detail_job_id = rh.detail_job_id
         WHERE a.kind = 'action' AND r.headline IS NOT NULL AND r.event_on IS NOT NULL AND s.total > 0;

        ALTER VIEW public_api.bills OWNER TO civic_lens_public_owner;

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
               SELECT DISTINCT ON (b.congress, b.bill_type, b.bill_number)
                      b.congress, b.bill_type, b.bill_number, b.record_id
                 FROM public_api.bills b
                ORDER BY b.congress, b.bill_type, b.bill_number, b.record_id
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
        """;

    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(UpSql);
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(DownSql);
}
