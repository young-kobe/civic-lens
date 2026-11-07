using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations;

public partial class PublicApiStatementLists : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE VIEW public_api.statements WITH (security_barrier = true) AS
        SELECT a.record_id, a.revision_number, r.statement_date, r.headline, r.summary,
               CASE r.author_kind WHEN 'Analysis' THEN 'Model' ELSE 'Person' END AS text_origin,
               CASE WHEN r.author_kind = 'Analysis' AND e.approved_by = 'Code' THEN 'An AI summary of this release.' END AS label,
               ARRAY(SELECT i.issue_id FROM public.statement_revision_issues i
                     WHERE i.candidate_id = r.candidate_id AND i.revision_number = r.revision_number ORDER BY i."position") AS issue_ids
          FROM public_api.active_records a
          JOIN public.statement_revisions r ON r.candidate_id = a.record_id AND r.revision_number = a.revision_number
          JOIN public.published_entries e ON e.kind = 'statement' AND e.record_id = a.record_id AND e.revision_number = a.revision_number
         WHERE a.kind = 'statement' AND r.headline IS NOT NULL AND r.summary IS NOT NULL AND e.approved_by IS NOT NULL
           AND EXISTS (SELECT 1 FROM public.statement_revision_speakers s
                        WHERE s.candidate_id = r.candidate_id AND s.revision_number = r.revision_number);
        ALTER VIEW public_api.statements OWNER TO civic_lens_public_owner;

        CREATE VIEW public_api.statement_speakers WITH (security_barrier = true) AS
        SELECT s.record_id, s.revision_number, p."position", p.official_id
          FROM public_api.statements s
          JOIN public.statement_revision_speakers p ON p.candidate_id = s.record_id AND p.revision_number = s.revision_number;
        ALTER VIEW public_api.statement_speakers OWNER TO civic_lens_public_owner;
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DROP VIEW public_api.statement_speakers;
        DROP VIEW public_api.statements;
        REVOKE SELECT (statement_date, summary) ON statement_revisions FROM civic_lens_public_owner;
        REVOKE SELECT (candidate_id, revision_number, official_id, "position") ON statement_revision_speakers FROM civic_lens_public_owner;
        REVOKE SELECT (candidate_id, revision_number, issue_id, "position") ON statement_revision_issues FROM civic_lens_public_owner;
        """);
}
