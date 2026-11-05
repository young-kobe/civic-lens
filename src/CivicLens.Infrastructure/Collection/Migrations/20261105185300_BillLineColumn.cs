using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations
{
    /// <inheritdoc />
    public partial class BillLineColumn : Migration
    {
        // The view drops and returns, as in ZipDistrictCongress. bill_line comes last. It is null when the active entry holds no line or the entry row
        // is not backfilled yet. The head of the view repeats PublicApiBillViews.
        private const string ViewHeadSql = """
            CREATE VIEW public_api.bills WITH (security_barrier = true) AS
            SELECT a.record_id, a.revision_number, a.release_number,
                   r.congress, r.bill_type, r.bill_number, r.bill_title, r.headline, r.summary, r.text_origin,
                   CASE r.text_origin
                     WHEN 'Model' THEN 'An AI summary of the official record'
                     WHEN 'Code' THEN 'Written automatically from Congress.gov data, with no AI'
                   END AS label,
                   r.policy_area, r.policy_area_id, r.event_on AS event_date, s.sponsor_count, r.cosponsor_count
            """;

        private const string ViewBodySql = """

              FROM public_api.active_records a
              JOIN public.action_revisions r ON r.record_id = a.record_id AND r.revision_number = a.revision_number
             CROSS JOIN LATERAL (
                   SELECT count(*) AS total, count(*) FILTER (WHERE p.role = 'Sponsor') AS sponsor_count
                     FROM public.action_revision_sponsorships p
                    WHERE p.record_id = r.record_id AND p.revision_number = r.revision_number) s
            """;

        private const string ViewTailSql = """

             WHERE a.kind = 'action' AND r.headline IS NOT NULL AND r.event_on IS NOT NULL AND s.total > 0;

            ALTER VIEW public_api.bills OWNER TO civic_lens_public_owner;
            """;

        private const string UpViewSql = "DROP VIEW public_api.bills;\n\n" + ViewHeadSql + ",\n                   e.bill_line" + ViewBodySql + """

              LEFT JOIN public.published_entries e ON e.kind = 'action' AND e.record_id = a.record_id AND e.revision_number = a.revision_number
            """ + ViewTailSql;

        private const string DownViewSql = "DROP VIEW public_api.bills;\n\n" + ViewHeadSql + ViewBodySql + ViewTailSql;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "bill_line",
                table: "published_entries",
                type: "text",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_published_entries_bill_line",
                table: "published_entries",
                sql: "bill_line IS NULL OR (kind = 'action' AND length(btrim(bill_line)) >= 1 AND length(bill_line) <= 200)");

            migrationBuilder.Sql(UpViewSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(DownViewSql);

            migrationBuilder.DropCheckConstraint(
                name: "ck_published_entries_bill_line",
                table: "published_entries");

            migrationBuilder.DropColumn(
                name: "bill_line",
                table: "published_entries");
        }
    }
}
