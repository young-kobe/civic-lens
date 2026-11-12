using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations;

/// <summary>
/// Adds the view <c>public_api.bill_sponsorship_parties</c>, the one SQL owner of the term party on the sponsorship date. A day that no term holds,
/// a blank term party, and terms of two parties on one day give no party. It also adds one column to <c>public_api.bills</c>: whether the summary
/// is model text drafted from the CRS summary. The bill page reads the checked time from <c>public_api.record_entries</c> and the first publication
/// time from <c>public_api.record_first_publications</c>, so it never copies the rules that pick them. The rollback drops the dependent views
/// <c>bill_actions</c>, <c>votes</c>, and <c>action_sources</c> and restores them from <c>BillHistoryProjectionVersion</c>, <c>ReleaseVoteBills</c>,
/// and <c>PublicApiSearchSources</c>.
/// </summary>
public partial class PublicApiBillSponsorPartiesAndTimes : Migration
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
               h.public_law, h.observed_at_utc_ticks AS history_observed_at_utc_ticks, h.action_count AS history_action_count,
               r.crs_summary_text, CASE WHEN r.crs_summary_text IS NOT NULL THEN r.summary_version END AS crs_summary_version,
               r.text_origin = 'Model' AND r.text_package_id IS NULL AND r.summary_version IS NOT NULL AS summary_from_crs
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
          LEFT JOIN public.published_bill_histories h ON h.detail_job_id = rh.detail_job_id AND h.projection_version = rh.projection_version
         WHERE a.kind = 'action' AND r.headline IS NOT NULL AND r.event_on IS NOT NULL
           AND EXISTS (SELECT 1 FROM public.action_revision_sponsorships eligible
                        WHERE eligible.record_id = r.record_id AND eligible.revision_number = r.revision_number);
        ALTER VIEW public_api.bills OWNER TO civic_lens_public_owner;

        CREATE VIEW public_api.bill_sponsorship_parties WITH (security_barrier = true) AS
        SELECT s.record_id, s.revision_number, s."position", term.party
          FROM public_api.bill_sponsorships s
         CROSS JOIN LATERAL (
               SELECT CASE WHEN count(DISTINCT held.party) = 1 AND count(held.party) = count(*) THEN min(held.party) END AS party
                 FROM (SELECT CASE WHEN btrim(t.party) <> '' THEN t.party END AS party
                         FROM public_api.release_official_terms t
                        WHERE t.official_id = s.official_id
                          AND t.start_on <= s.sponsored_on AND (t.end_on IS NULL OR t.end_on >= s.sponsored_on)) held) term;
        ALTER VIEW public_api.bill_sponsorship_parties OWNER TO civic_lens_public_owner;
        """;

    private static readonly string DownSql = """
        DROP VIEW public_api.bill_sponsorship_parties;
        DROP VIEW public_api.bill_actions, public_api.votes, public_api.action_sources;
        DROP VIEW public_api.bills;
        CREATE VIEW public_api.bills WITH (security_barrier = true) AS
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
               h.public_law, h.observed_at_utc_ticks AS history_observed_at_utc_ticks, h.action_count AS history_action_count,
               r.crs_summary_text, CASE WHEN r.crs_summary_text IS NOT NULL THEN r.summary_version END AS crs_summary_version
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
          LEFT JOIN public.published_bill_histories h ON h.detail_job_id = rh.detail_job_id AND h.projection_version = rh.projection_version
         WHERE a.kind = 'action' AND r.headline IS NOT NULL AND r.event_on IS NOT NULL
           AND EXISTS (SELECT 1 FROM public.action_revision_sponsorships eligible
                        WHERE eligible.record_id = r.record_id AND eligible.revision_number = r.revision_number);
        ALTER VIEW public_api.bills OWNER TO civic_lens_public_owner;

        CREATE VIEW public_api.bill_actions WITH (security_barrier = true) AS
        SELECT b.release_number, b.record_id, b.revision_number, a.position, a.action_date, a.action_time,
               a.text, a.type, a.action_code, a.source_system_code, a.source_system_name
          FROM public_api.bills b
          JOIN public.release_bill_histories r ON r.release_number = b.release_number AND r.record_id = b.record_id
          JOIN public.published_bill_history_actions a ON a.detail_job_id = r.detail_job_id AND a.projection_version = r.projection_version;
        ALTER VIEW public_api.bill_actions OWNER TO civic_lens_public_owner;

        CREATE VIEW public_api.votes WITH (security_barrier = true) AS
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
               bill.record_id::varchar(64) AS bill_record_id
          FROM public_api.active_records a
          JOIN public.vote_revisions v ON v.record_id = a.record_id AND v.revision_number = a.revision_number
          LEFT JOIN public.release_vote_bills rb ON rb.release_number = a.release_number AND rb.vote_record_id = a.record_id
          LEFT JOIN LATERAL (SELECT b.record_id FROM public_api.bills b WHERE b.record_id = rb.bill_record_id LIMIT 1) bill ON TRUE
          JOIN public.vote_member_lists t ON t.list_id = v.member_list_id
         WHERE a.kind = 'vote' AND v.member_list_id IS NOT NULL;
        ALTER VIEW public_api.votes OWNER TO civic_lens_public_owner;
        """ + PublicApiSearchSources.ActionSources + """

        GRANT SELECT ON public_api.bills, public_api.bill_actions, public_api.votes, public_api.action_sources TO civic_lens_public;
        """;

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(UpSql);

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(DownSql);
}
