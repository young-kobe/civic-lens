using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations;

/// <summary>Adds the coverage counts, the cutoff release, the approval time, and the first publication time of a brief to <c>public_api.briefs</c>.
/// The server-rendered brief page shows them, as the static page does. The approval time is the published approval time of the active revision
/// in <c>published_entries</c>, so a later decision changes nothing until a new release. It reads the base table, not <c>record_entries</c>, so the rollback of <c>PublishedEntryApprovalTime</c> can drop and recreate that view.
/// The two times are null when the base rows hold none. <c>PublicApiGrants</c> owns the column grants.</summary>
public partial class PublicApiBriefCoverage : Migration
{
    private const string PublishedApprovalTime = """
        (SELECT e.approved_at_utc_ticks FROM public.published_entries e
                 WHERE e.kind = 'brief' AND e.record_id = a.record_id AND e.revision_number = a.revision_number)
        """;

    private static string ViewSql(string approvalTime) => $"""
        CREATE OR REPLACE VIEW public_api.briefs WITH (security_barrier = true) AS
        SELECT a.record_id, a.revision_number, a.release_number, r.week_start, r.headline, r.text_origin,
               CASE r.text_origin WHEN 'Model' THEN 'Drafted by an AI model from reviewed records. A person checked it before publication.' END AS label,
               r.cutoff_release_number, r.coverage_considered, r.coverage_sent_to_model,
               {approvalTime} AS approved_at_utc_ticks,
               (SELECT p.first_published_at_utc_ticks FROM public.published_records p
                 WHERE p.kind = 'brief' AND p.record_id = a.record_id) AS first_published_at_utc_ticks
          FROM public_api.active_records a
          JOIN public.brief_revisions r ON r.record_id = a.record_id AND r.revision_number = a.revision_number
         WHERE a.kind = 'brief' AND r.week_start IS NOT NULL AND r.headline IS NOT NULL AND r.text_origin IS NOT NULL;
        """;

    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(ViewSql(PublishedApprovalTime));

    // The views brief_paragraphs and brief_citations read public_api.briefs, so the view cannot drop. The rollback keeps the added columns. Older code ignores them.
    // The approval column becomes null, so the rollback of PublishedEntryApprovalTime can drop the base column that it read.
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(ViewSql("NULL::bigint"));
}
