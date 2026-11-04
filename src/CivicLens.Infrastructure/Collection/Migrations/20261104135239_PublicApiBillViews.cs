using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations
{
    /// <inheritdoc />
    public partial class PublicApiBillViews : Migration
    {
        // A revision shows only when its columns, its derived columns, and a sponsorship exist. An older revision holds revision_json alone.
        // event_date and cosponsor_count read the columns that ActionRevisionRows writes. The wording repeats PublishedRecordLabels.
        private const string UpSql = """
            CREATE VIEW public_api.bills WITH (security_barrier = true) AS
            SELECT a.record_id, a.revision_number, a.release_number,
                   r.congress, r.bill_type, r.bill_number, r.bill_title, r.headline, r.summary, r.text_origin,
                   CASE r.text_origin
                     WHEN 'Model' THEN 'An AI summary of the official record'
                     WHEN 'Code' THEN 'Written automatically from Congress.gov data, with no AI'
                   END AS label,
                   r.policy_area, r.policy_area_id, r.event_on AS event_date, s.sponsor_count, r.cosponsor_count
              FROM public_api.active_records a
              JOIN public.action_revisions r ON r.record_id = a.record_id AND r.revision_number = a.revision_number
             CROSS JOIN LATERAL (
                   SELECT count(*) AS total, count(*) FILTER (WHERE p.role = 'Sponsor') AS sponsor_count
                     FROM public.action_revision_sponsorships p
                    WHERE p.record_id = r.record_id AND p.revision_number = r.revision_number) s
             WHERE a.kind = 'action' AND r.headline IS NOT NULL AND r.event_on IS NOT NULL AND s.total > 0;

            CREATE VIEW public_api.bill_sponsorships WITH (security_barrier = true) AS
            SELECT a.record_id, a.revision_number, p."position", p.person_id AS official_id, p.role, p.is_original, p.sponsored_on, p.withdrawn_on
              FROM public_api.active_records a
              JOIN public.action_revisions r ON r.record_id = a.record_id AND r.revision_number = a.revision_number
              JOIN public.action_revision_sponsorships p ON p.record_id = r.record_id AND p.revision_number = r.revision_number
             WHERE a.kind = 'action' AND r.headline IS NOT NULL AND r.event_on IS NOT NULL;

            CREATE VIEW public_api.bill_issues WITH (security_barrier = true) AS
            SELECT a.record_id, a.revision_number, i.issue_id, i."position"
              FROM public_api.active_records a
              JOIN public.action_revisions r ON r.record_id = a.record_id AND r.revision_number = a.revision_number
              JOIN public.action_revision_issues i ON i.record_id = r.record_id AND i.revision_number = r.revision_number
             WHERE a.kind = 'action' AND r.headline IS NOT NULL AND r.event_on IS NOT NULL AND i.is_tracked
               AND EXISTS (SELECT 1 FROM public.action_revision_sponsorships p
                            WHERE p.record_id = r.record_id AND p.revision_number = r.revision_number);

            ALTER VIEW public_api.bills OWNER TO civic_lens_public_owner;
            ALTER VIEW public_api.bill_sponsorships OWNER TO civic_lens_public_owner;
            ALTER VIEW public_api.bill_issues OWNER TO civic_lens_public_owner;
            """;

        private const string DownSql = """
            DROP VIEW public_api.bills, public_api.bill_sponsorships, public_api.bill_issues;
            REVOKE SELECT ON action_revisions, action_revision_sponsorships, action_revision_issues FROM civic_lens_public_owner;
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(UpSql);

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(DownSql);
    }
}
