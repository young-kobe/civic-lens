using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations;

/// <summary>Stores the approval time of each published revision in <c>published_entries</c>, as the approver is stored. The column is null until the entries backfill fills it.
/// The view <c>public_api.record_entries</c> shows it as the last column, so a public page reads the stored time and never copies the rule that picks it.</summary>
public partial class PublishedEntryApprovalTime : Migration
{
    private const string EntriesViewBody = """
        SELECT a.record_id, a.kind, a.revision_number, e.headline, e.source_host, e.event_date, e.bill_line,
               e.bill_title, e.document_type, e.link_type, e.statement_kind, e.statement_record_id, e.target_kind, e.target_record_id
        """;

    private const string EntriesViewFrom = """
          FROM public_api.active_records a
          JOIN public.published_entries e ON e.kind = a.kind AND e.record_id = a.record_id AND e.revision_number = a.revision_number;
        """;

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<long>(name: "approved_at_utc_ticks", table: "published_entries", type: "bigint", nullable: true);
        migrationBuilder.AddCheckConstraint(name: "ck_published_entries_approved_at_utc_ticks", table: "published_entries",
            sql: "approved_at_utc_ticks BETWEEN 0 AND 3155378975999999999");
        // The new column goes last, so the view keeps its definition and its grants. The next db migrate grants the column to the view owner.
        migrationBuilder.Sql($"""
            CREATE OR REPLACE VIEW public_api.record_entries WITH (security_barrier = true) AS
            {EntriesViewBody}, e.approved_at_utc_ticks
            {EntriesViewFrom}
            """);
    }

    // Postgres cannot drop a view column in place, so the old view replaces the new one. Nothing reads record_entries, so no dependent view goes with it.
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql($"""
            DROP VIEW public_api.record_entries;
            CREATE VIEW public_api.record_entries WITH (security_barrier = true) AS
            {EntriesViewBody}
            {EntriesViewFrom}
            ALTER VIEW public_api.record_entries OWNER TO civic_lens_public_owner;
            DO $$
            BEGIN
              IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'civic_lens_public') THEN
                GRANT SELECT ON public_api.record_entries TO civic_lens_public;
              END IF;
            END $$;
            """);
        migrationBuilder.DropCheckConstraint(name: "ck_published_entries_approved_at_utc_ticks", table: "published_entries");
        migrationBuilder.DropColumn(name: "approved_at_utc_ticks", table: "published_entries");
    }
}
