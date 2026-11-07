using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations;

public partial class PublicApiBriefContent : Migration
{
    private const string UpSql = """
        CREATE VIEW public_api.brief_paragraphs WITH (security_barrier = true) AS
        SELECT b.record_id, b.revision_number, b.release_number, p.position, p.paragraph_text,
               ARRAY(SELECT pc.citation_id FROM public.brief_revision_paragraph_citations pc
                      WHERE pc.record_id = p.record_id AND pc.revision_number = p.revision_number
                        AND pc.part = 'Text' AND pc.paragraph_position = p.position
                      ORDER BY pc.position) AS citation_ids
          FROM public_api.briefs b
          JOIN public.brief_revision_paragraphs p ON p.record_id = b.record_id AND p.revision_number = b.revision_number
         WHERE p.part = 'Text';

        CREATE VIEW public_api.brief_citations WITH (security_barrier = true) AS
        SELECT b.record_id, b.revision_number, b.release_number, c.position, c.citation_id,
               CASE c.kind WHEN 'Statement' THEN 'statement' WHEN 'Action' THEN 'action' WHEN 'DocumentChange' THEN 'documentChange' END AS kind,
               CASE c.kind WHEN 'Statement' THEN c.statement_candidate_id WHEN 'Action' THEN c.action_record_id WHEN 'DocumentChange' THEN c.document_change_draft_id END AS target_record_id,
               CASE c.kind WHEN 'Statement' THEN c.statement_revision_number WHEN 'Action' THEN c.action_revision_number WHEN 'DocumentChange' THEN c.document_change_revision_number END AS target_revision_number,
               CASE c.kind WHEN 'Statement' THEN s.headline WHEN 'Action' THEN ar.headline WHEN 'DocumentChange' THEN d.headline END AS headline
          FROM public_api.briefs b
          JOIN public.brief_revision_citations c ON c.record_id = b.record_id AND c.revision_number = b.revision_number AND c.part = 'Text'
          LEFT JOIN public.statement_revisions s ON c.kind = 'Statement' AND s.candidate_id = c.statement_candidate_id AND s.revision_number = c.statement_revision_number
          LEFT JOIN public.action_revisions ar ON c.kind = 'Action' AND ar.record_id = c.action_record_id AND ar.revision_number = c.action_revision_number
          LEFT JOIN public.document_change_revisions d ON c.kind = 'DocumentChange' AND d.draft_id = c.document_change_draft_id AND d.revision_number = c.document_change_revision_number;

        ALTER VIEW public_api.brief_paragraphs OWNER TO civic_lens_public_owner;
        ALTER VIEW public_api.brief_citations OWNER TO civic_lens_public_owner;
        """;

    private const string DownSql = """
        DROP VIEW public_api.brief_citations, public_api.brief_paragraphs;
        REVOKE SELECT (record_id, revision_number, part, position, paragraph_text) ON brief_revision_paragraphs FROM civic_lens_public_owner;
        REVOKE SELECT (record_id, revision_number, part, citation_id, position, kind, statement_candidate_id, statement_revision_number,
            action_record_id, action_revision_number, document_change_draft_id, document_change_revision_number) ON brief_revision_citations FROM civic_lens_public_owner;
        REVOKE SELECT (record_id, revision_number, part, paragraph_position, position, citation_id) ON brief_revision_paragraph_citations FROM civic_lens_public_owner;
        REVOKE SELECT (draft_id, revision_number, headline) ON document_change_revisions FROM civic_lens_public_owner;
        """;

    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(UpSql);
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(DownSql);
}
