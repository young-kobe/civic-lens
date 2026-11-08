using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations;

public partial class PublicApiStatementDetail : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE VIEW public_api.statement_sources WITH (security_barrier = true) AS
        SELECT s.record_id, s.revision_number, c.extraction_id, a.requested_url AS url,
               a.observed_at_utc_ticks, e.text,
               CASE WHEN c.url <> a.requested_url THEN c.url END AS article_url,
               r.statement_date_evidence_extraction_id, r.statement_date_evidence_start, r.statement_date_evidence_length
          FROM public_api.statements s
          JOIN public.statement_candidates c ON c.candidate_id = s.record_id
          JOIN public.statement_revisions r ON r.candidate_id = s.record_id AND r.revision_number = s.revision_number
          JOIN public.document_extractions e ON e.extraction_id = c.extraction_id
          JOIN public.collection_attempts a ON a.attempt_id = e.attempt_id AND a.attempt_id = c.attempt_id;
        ALTER VIEW public_api.statement_sources OWNER TO civic_lens_public_owner;

        CREATE VIEW public_api.statement_quotations WITH (security_barrier = true) AS
        SELECT s.record_id, s.revision_number, q.position, q.quotation_extraction_id AS extraction_id,
               q.quotation_start, q.quotation_length, q.kind
          FROM public_api.statements s
          JOIN public.statement_revision_quotations q
            ON q.candidate_id = s.record_id AND q.revision_number = s.revision_number;
        ALTER VIEW public_api.statement_quotations OWNER TO civic_lens_public_owner;
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DROP VIEW public_api.statement_quotations;
        DROP VIEW public_api.statement_sources;
        REVOKE SELECT (statement_date_evidence_extraction_id, statement_date_evidence_start, statement_date_evidence_length)
            ON statement_revisions FROM civic_lens_public_owner;
        REVOKE SELECT (candidate_id, url, extraction_id, attempt_id) ON statement_candidates FROM civic_lens_public_owner;
        REVOKE SELECT (extraction_id, attempt_id, text) ON document_extractions FROM civic_lens_public_owner;
        REVOKE SELECT (requested_url) ON collection_attempts FROM civic_lens_public_owner;
        REVOKE SELECT (candidate_id, revision_number, position, quotation_extraction_id, quotation_start, quotation_length, kind)
            ON statement_revision_quotations FROM civic_lens_public_owner;
        """);
}
