using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations
{
    /// <inheritdoc />
    public partial class ActionRevisionBillLine : Migration
    {
        private const string ViewHeadSql = """
            CREATE VIEW public_api.bills WITH (security_barrier = true) AS
            SELECT a.record_id, a.revision_number, a.release_number,
                   r.congress, r.bill_type, r.bill_number, r.bill_title, r.headline, r.summary, r.text_origin,
                   CASE
                     WHEN r.text_origin = 'Model' AND r.text_package_id IS NOT NULL THEN 'AI summary of the bill text. An AI model wrote this. Each sentence comes with an exact quote from the text. Code checked the quotes, not the meaning of the sentences.'
                     WHEN r.text_origin = 'Model' THEN 'An AI summary of the official record'
                     WHEN r.text_origin = 'Code' THEN 'Written automatically from Congress.gov data, with no AI'
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

        private const string TextColumnsSql = """

            ,
                   CASE WHEN r.text_origin = 'Model' THEN r.why_it_matters END AS why_it_matters,
                   t.version_type AS text_version_name, t.version_date AS text_version_date, t.chars_read AS text_chars_read, t.chars_total AS text_chars_total
            """;

        private const string TextJoinSql = """

              LEFT JOIN public.bill_text_versions t ON t.congress = r.congress AND t.bill_type = r.bill_type AND t.bill_number = r.bill_number
                                                   AND t.package_id = r.text_package_id
            """;

        private const string ViewTailSql = """

             WHERE a.kind = 'action' AND r.headline IS NOT NULL AND r.event_on IS NOT NULL AND s.total > 0;

            ALTER VIEW public_api.bills OWNER TO civic_lens_public_owner;
            """;

        private const string UpViewSql = "DROP VIEW public_api.bills;\n\n" + ViewHeadSql + TextColumnsSql + ",\n                   r.bill_line,\n                   CASE WHEN r.bill_line IS NOT NULL THEN 'Model' END AS bill_line_origin,\n                   CASE WHEN r.bill_line IS NOT NULL THEN 'AI summary of the Congressional Research Service summary.' END AS bill_line_label" + ViewBodySql + TextJoinSql + ViewTailSql + "\n\nREVOKE SELECT (bill_line) ON public.published_entries FROM civic_lens_public_owner;";

        private const string DownViewSql = "GRANT SELECT (bill_line) ON public.published_entries TO civic_lens_public_owner;\n\nDROP VIEW public_api.bills;\n\n" + ViewHeadSql + ",\n                   e.bill_line" + TextColumnsSql + ViewBodySql + "\n              LEFT JOIN public.published_entries e ON e.kind = 'action' AND e.record_id = a.record_id AND e.revision_number = a.revision_number\n" + TextJoinSql + ViewTailSql;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "bill_line",
                table: "action_revisions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(name: "bill_line_citation_extraction_id", table: "action_revisions", type: "character varying(64)", maxLength: 64, nullable: true);
            migrationBuilder.AddColumn<int>(name: "bill_line_citation_start", table: "action_revisions", type: "integer", nullable: true);
            migrationBuilder.AddColumn<int>(name: "bill_line_citation_length", table: "action_revisions", type: "integer", nullable: true);
            migrationBuilder.CreateIndex(name: "IX_action_revisions_bill_line_citation_extraction_id", table: "action_revisions", column: "bill_line_citation_extraction_id");
            migrationBuilder.AddForeignKey(name: "FK_action_revisions_document_extractions_bill_line_citation_ex~", table: "action_revisions",
                column: "bill_line_citation_extraction_id", principalTable: "document_extractions", principalColumn: "extraction_id", onDelete: ReferentialAction.Restrict);
            migrationBuilder.AddCheckConstraint(name: "ck_action_revisions_bill_line_citation", table: "action_revisions",
                sql: "(bill_line_citation_extraction_id IS NULL) = (bill_line_citation_start IS NULL) AND (bill_line_citation_extraction_id IS NULL) = (bill_line_citation_length IS NULL) AND bill_line_citation_start >= 0 AND bill_line_citation_length > 0");
            migrationBuilder.AddCheckConstraint(name: "ck_action_revisions_bill_line", table: "action_revisions",
                sql: "(bill_line IS NULL) = (bill_line_citation_extraction_id IS NULL) AND (bill_line IS NULL OR (summary_citation_extraction_id IS NOT NULL AND bill_line_citation_extraction_id = summary_citation_extraction_id AND bill_line_citation_start >= summary_citation_start AND bill_line_citation_length <= summary_citation_length AND bill_line_citation_start - summary_citation_start <= summary_citation_length - bill_line_citation_length AND length(btrim(bill_line)) >= 1 AND length(bill_line) <= 200))");

            migrationBuilder.Sql(UpViewSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(DownViewSql);

            migrationBuilder.DropCheckConstraint(name: "ck_action_revisions_bill_line", table: "action_revisions");
            migrationBuilder.DropCheckConstraint(name: "ck_action_revisions_bill_line_citation", table: "action_revisions");
            migrationBuilder.DropForeignKey(name: "FK_action_revisions_document_extractions_bill_line_citation_ex~", table: "action_revisions");
            migrationBuilder.DropIndex(name: "IX_action_revisions_bill_line_citation_extraction_id", table: "action_revisions");
            migrationBuilder.DropColumn(name: "bill_line_citation_extraction_id", table: "action_revisions");
            migrationBuilder.DropColumn(name: "bill_line_citation_start", table: "action_revisions");
            migrationBuilder.DropColumn(name: "bill_line_citation_length", table: "action_revisions");
            migrationBuilder.DropColumn(name: "bill_line", table: "action_revisions");
        }
    }
}
