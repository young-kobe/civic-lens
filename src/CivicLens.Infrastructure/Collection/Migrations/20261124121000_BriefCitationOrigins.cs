using CivicLens.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations;

[DbContext(typeof(CollectionAttemptDbContext))]
[Migration("20261124121000_BriefCitationOrigins")]
public sealed class BriefCitationOrigins : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(WithOrigins);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(WithoutOrigins);

    private const string WithOrigins = """
        CREATE OR REPLACE VIEW public_api.brief_citations WITH (security_barrier = true) AS
        SELECT b.record_id, b.revision_number, b.release_number, c.position, c.citation_id,
               CASE c.kind WHEN 'Statement' THEN 'statement' WHEN 'Action' THEN 'action' WHEN 'DocumentChange' THEN 'documentChange' END AS kind,
               CASE c.kind WHEN 'Statement' THEN c.statement_candidate_id WHEN 'Action' THEN c.action_record_id WHEN 'DocumentChange' THEN c.document_change_draft_id END AS target_record_id,
               CASE c.kind WHEN 'Statement' THEN c.statement_revision_number WHEN 'Action' THEN c.action_revision_number WHEN 'DocumentChange' THEN c.document_change_revision_number END AS target_revision_number,
               CASE c.kind WHEN 'Statement' THEN s.headline WHEN 'Action' THEN ar.headline WHEN 'DocumentChange' THEN d.headline END AS headline,
               CASE c.kind
                   WHEN 'Statement' THEN CASE s.author_kind WHEN 'Analysis' THEN 'Model' WHEN 'Code' THEN 'Code' WHEN 'Person' THEN 'Person' END
                   WHEN 'Action' THEN ar.text_origin
                   WHEN 'DocumentChange' THEN CASE d.author_kind WHEN 'Analysis' THEN 'Model' WHEN 'Person' THEN 'Person' END
               END AS target_text_origin
          FROM public_api.briefs b
          JOIN public.brief_revision_citations c ON c.record_id = b.record_id AND c.revision_number = b.revision_number AND c.part = 'Text'
          LEFT JOIN public.statement_revisions s ON c.kind = 'Statement' AND s.candidate_id = c.statement_candidate_id AND s.revision_number = c.statement_revision_number
          LEFT JOIN public.action_revisions ar ON c.kind = 'Action' AND ar.record_id = c.action_record_id AND ar.revision_number = c.action_revision_number
          LEFT JOIN public.document_change_revisions d ON c.kind = 'DocumentChange' AND d.draft_id = c.document_change_draft_id AND d.revision_number = c.document_change_revision_number;
        """;

    private const string WithoutOrigins = """
        DROP VIEW public_api.brief_citations;
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
        ALTER VIEW public_api.brief_citations OWNER TO civic_lens_public_owner;
        GRANT SELECT ON public_api.brief_citations TO civic_lens_public;
        """;
}
