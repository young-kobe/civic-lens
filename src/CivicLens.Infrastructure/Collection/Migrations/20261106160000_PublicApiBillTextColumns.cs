using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations
{
    /// <inheritdoc />
    public partial class PublicApiBillTextColumns : Migration
    {
        // The view drops and returns, as in BillLineColumn. The new columns come last. A summary that an AI model wrote from the bill text has its
        // own label. why_it_matters is model text, so the view serves it only for the origin Model, the origin that carries the label. The text_*
        // columns come from the stored row of the bill text version that the revision cites, and they are null for a revision that cites none.
        // The label text copies BillTextWords.Label. BillTextWordsTests compares the two, because SQL cannot read the class.
        private const string ViewHeadSql = """
            CREATE VIEW public_api.bills WITH (security_barrier = true) AS
            SELECT a.record_id, a.revision_number, a.release_number,
                   r.congress, r.bill_type, r.bill_number, r.bill_title, r.headline, r.summary, r.text_origin,
                   CASE
                     WHEN r.text_origin = 'Model' AND r.text_package_id IS NOT NULL THEN 'AI summary of the bill text. An AI model wrote this. Code checked that each sentence quotes the text.'
                     WHEN r.text_origin = 'Model' THEN 'An AI summary of the official record'
                     WHEN r.text_origin = 'Code' THEN 'Written automatically from Congress.gov data, with no AI'
                   END AS label,
                   r.policy_area, r.policy_area_id, r.event_on AS event_date, s.sponsor_count, r.cosponsor_count, e.bill_line
            """;

        private const string ViewBodySql = """

              FROM public_api.active_records a
              JOIN public.action_revisions r ON r.record_id = a.record_id AND r.revision_number = a.revision_number
             CROSS JOIN LATERAL (
                   SELECT count(*) AS total, count(*) FILTER (WHERE p.role = 'Sponsor') AS sponsor_count
                     FROM public.action_revision_sponsorships p
                    WHERE p.record_id = r.record_id AND p.revision_number = r.revision_number) s
              LEFT JOIN public.published_entries e ON e.kind = 'action' AND e.record_id = a.record_id AND e.revision_number = a.revision_number
            """;

        private const string ViewTailSql = """

             WHERE a.kind = 'action' AND r.headline IS NOT NULL AND r.event_on IS NOT NULL AND s.total > 0;

            ALTER VIEW public_api.bills OWNER TO civic_lens_public_owner;
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

        private const string UpViewSql = "DROP VIEW public_api.bills;\n\n" + ViewHeadSql + TextColumnsSql + ViewBodySql + TextJoinSql + ViewTailSql;

        private const string DownViewSql = "DROP VIEW public_api.bills;\n\n" + ViewHeadSql + ViewBodySql + ViewTailSql;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(UpViewSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(DownViewSql);
        }
    }
}
