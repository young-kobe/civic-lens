using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations;

/// <summary>
/// A bill line that ends with the cut mark is not shown anywhere. The views <c>public_api.bills</c> and <c>public_api.record_entries</c> give null for such a line, so every
/// public read has one owner of the rule. Earlier code cut a long first sentence at a word boundary and stored the line. Published entry rows never change, so those lines
/// stay in <c>published_entries</c>. The columns, their order, and the grants stay the same. The rollback restores the plain <c>bill_line</c> column.
/// </summary>
public partial class HideCutBillLines : Migration
{
    private const string StoredLine = "e.bill_line";

    private const string WholeLine = "CASE WHEN right(e.bill_line, 1) <> '…' THEN e.bill_line END";

    private static string BillsView(string line) => $"""
        CREATE OR REPLACE VIEW public_api.bills WITH (security_barrier = true) AS
        SELECT a.record_id, a.revision_number, a.release_number,
               r.congress, r.bill_type, r.bill_number, r.bill_title, r.headline, r.summary, r.text_origin,
               CASE
                 WHEN r.text_origin = 'Model' AND r.text_package_id IS NOT NULL THEN 'AI summary of the bill text. An AI model wrote this. Each sentence comes with an exact quote from the text. Code checked the quotes, not the meaning of the sentences.'
                 WHEN r.text_origin = 'Model' THEN 'An AI summary of the official record'
                 WHEN r.text_origin = 'Code' THEN 'Written automatically from Congress.gov data, with no AI'
               END AS label,
               r.policy_area, r.policy_area_id, r.event_on AS event_date, s.sponsor_count, r.cosponsor_count, {line} AS bill_line,
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

        """;

    private static string EntriesView(string line) => $"""
        CREATE OR REPLACE VIEW public_api.record_entries WITH (security_barrier = true) AS
        SELECT a.record_id, a.kind, a.revision_number, e.headline, e.source_host, e.event_date, {line} AS bill_line,
               e.bill_title, e.document_type, e.link_type, e.statement_kind, e.statement_record_id, e.target_kind, e.target_record_id, e.approved_at_utc_ticks
          FROM public_api.active_records a
          JOIN public.published_entries e ON e.kind = a.kind AND e.record_id = a.record_id AND e.revision_number = a.revision_number;
        """;

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(BillsView(WholeLine) + EntriesView(WholeLine));

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(BillsView(StoredLine) + EntriesView(StoredLine));
}
