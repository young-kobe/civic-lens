using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations;

/// <summary>Exposes the published entry facts of each active record revision and the first publication time of each active record.
/// Each entry view carries every fact that the search ingest needs to build and check a <c>PublishedRecordEntry</c>: the bill title, the document type, the link columns, and the matched official. The search ingest will read them instead of the manifest. <c>PublishedEntries</c> keeps the entry rows equal to the manifest entries.</summary>
public partial class PublicApiSearchEntries : Migration
{
    private static string ChildView(string view, string table, string column, string extra = "") => $"""
        CREATE VIEW public_api.{view} WITH (security_barrier = true) AS
        SELECT a.record_id, a.kind, a.revision_number, c."position", c.{column}{extra}
          FROM public_api.active_records a
          JOIN public.{table} c ON c.kind = a.kind AND c.record_id = a.record_id AND c.revision_number = a.revision_number;
        ALTER VIEW public_api.{view} OWNER TO civic_lens_public_owner;
        """;

    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql($"""
        CREATE VIEW public_api.record_entries WITH (security_barrier = true) AS
        SELECT a.record_id, a.kind, a.revision_number, e.headline, e.source_host, e.event_date, e.bill_line,
               e.bill_title, e.document_type, e.link_type, e.statement_kind, e.statement_record_id, e.target_kind, e.target_record_id
          FROM public_api.active_records a
          JOIN public.published_entries e ON e.kind = a.kind AND e.record_id = a.record_id AND e.revision_number = a.revision_number;
        ALTER VIEW public_api.record_entries OWNER TO civic_lens_public_owner;
        {ChildView("record_officials", "published_entry_officials", "official_id", ", c.is_matched")}
        {ChildView("record_issues", "published_entry_issues", "issue_id")}
        {ChildView("record_policy_areas", "published_entry_policy_areas", "policy_area_id")}
        CREATE VIEW public_api.record_first_publications WITH (security_barrier = true) AS
        SELECT a.record_id, a.kind, p.first_published_at_utc_ticks
          FROM public_api.active_records a
          JOIN public.published_records p ON p.kind = a.kind AND p.record_id = a.record_id;
        ALTER VIEW public_api.record_first_publications OWNER TO civic_lens_public_owner;
        """);

    // The view withdrawals still reads published_records, and record_links still reads the older entry columns, so those grants stay.
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DROP VIEW public_api.record_first_publications;
        DROP VIEW public_api.record_policy_areas;
        DROP VIEW public_api.record_issues;
        DROP VIEW public_api.record_officials;
        DROP VIEW public_api.record_entries;
        REVOKE SELECT (headline, source_host, bill_title) ON published_entries FROM civic_lens_public_owner;
        REVOKE SELECT ("position", is_matched) ON published_entry_officials FROM civic_lens_public_owner;
        REVOKE SELECT (kind, record_id, revision_number, "position", issue_id) ON published_entry_issues FROM civic_lens_public_owner;
        REVOKE SELECT (kind, record_id, revision_number, "position", policy_area_id) ON published_entry_policy_areas FROM civic_lens_public_owner;
        """);
}
