using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations;

/// <summary>
/// Adds <c>action_revisions.introduced_on</c>, the introduced date of the Congress.gov bill record that the stored detail page of the candidate holds.
/// C# writes it with each revision, and <c>actions revisions backfill</c> fills it for older revisions. The column is nullable, and no view leaves out a
/// revision without it, so no bill leaves the public reads before or after the backfill. The view <c>public_api.bills</c> gets the column at its end.
/// The rollback drops the views that read the bill view and restores them and the bill view from <see cref="PublicApiBillSponsorPartiesAndTimes"/> and
/// <see cref="PublicApiBillKeys"/>.
/// </summary>
public partial class ActionRevisionIntroducedOn : Migration
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
               r.text_origin = 'Model' AND r.text_package_id IS NULL AND r.summary_version IS NOT NULL AS summary_from_crs,
               r.introduced_on
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
        """;

    private static readonly string DownSql = """
        DROP VIEW public_api.bill_actions, public_api.votes, public_api.action_sources, public_api.bill_keys;
        DROP VIEW public_api.bills;
        """ + PublicApiBillSponsorPartiesAndTimes.BillsView + PublicApiBillSponsorPartiesAndTimes.BillDependentViews + PublicApiBillKeys.BillKeysView + """

        GRANT SELECT ON public_api.bills, public_api.bill_actions, public_api.votes, public_api.action_sources, public_api.bill_keys TO civic_lens_public;
        """;

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateOnly>(name: "introduced_on", table: "action_revisions", type: "date", nullable: true);
        migrationBuilder.Sql(UpSql);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(DownSql);
        migrationBuilder.DropColumn(name: "introduced_on", table: "action_revisions");
    }
}
