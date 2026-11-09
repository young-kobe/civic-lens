using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations;

public partial class PublicApiDocumentChangeDetail : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE VIEW public_api.document_changes WITH (security_barrier = true) AS
        SELECT a.record_id, a.revision_number, a.release_number, r.comparison_id, c.hunk_count,
               r.headline, r.summary,
               CASE r.author_kind WHEN 'Analysis' THEN 'Model' WHEN 'Person' THEN 'Person' END AS text_origin,
               CASE r.author_kind WHEN 'Analysis' THEN 'Drafted by an AI model from these source versions. A person checked it before publication.' END AS label,
               r.significance, r.limits,
               r.institution, r.change_date,
               ARRAY(SELECT i.issue_id FROM public.document_change_revision_issues i
                     WHERE i.draft_id = a.record_id AND i.revision_number = a.revision_number
                     ORDER BY i.position, i.issue_id LIMIT 65) AS issue_ids
          FROM public_api.active_records a
          JOIN public.document_change_revisions r ON r.draft_id = a.record_id AND r.revision_number = a.revision_number
          LEFT JOIN public.document_comparisons c ON c.comparison_id = r.comparison_id
         WHERE a.kind = 'documentChange';
        ALTER VIEW public_api.document_changes OWNER TO civic_lens_public_owner;

        CREATE VIEW public_api.document_change_officials WITH (security_barrier = true) AS
        SELECT d.record_id, d.revision_number, o.position, o.official_id
          FROM public_api.document_changes d
          JOIN public.document_change_revision_officials o
            ON o.draft_id = d.record_id AND o.revision_number = d.revision_number;
        ALTER VIEW public_api.document_change_officials OWNER TO civic_lens_public_owner;

        CREATE VIEW public_api.document_change_sources WITH (security_barrier = true) AS
        SELECT d.record_id, d.revision_number, 'Before'::text AS source_side, c.before_extraction_id AS extraction_id,
               a.final_url AS url, a.observed_at_utc_ticks, e.text
          FROM public_api.document_changes d
          JOIN public.document_comparisons c ON c.comparison_id = d.comparison_id
          JOIN public.document_extractions e ON e.extraction_id = c.before_extraction_id
          JOIN public.collection_attempts a ON a.attempt_id = e.attempt_id
        UNION ALL
        SELECT d.record_id, d.revision_number, 'After'::text, c.after_extraction_id,
               a.final_url, a.observed_at_utc_ticks, e.text
          FROM public_api.document_changes d
          JOIN public.document_comparisons c ON c.comparison_id = d.comparison_id
          JOIN public.document_extractions e ON e.extraction_id = c.after_extraction_id
          JOIN public.collection_attempts a ON a.attempt_id = e.attempt_id;
        ALTER VIEW public_api.document_change_sources OWNER TO civic_lens_public_owner;

        CREATE VIEW public_api.document_change_hunks WITH (security_barrier = true) AS
        SELECT d.record_id, d.revision_number, h.position, h.before_start, h.before_length, h.after_start, h.after_length,
               h.before_context_start, h.before_context_length, h.after_context_start, h.after_context_length, h.word_edit_count
          FROM public_api.document_changes d
          JOIN public.document_comparison_hunks h ON h.comparison_id = d.comparison_id;
        ALTER VIEW public_api.document_change_hunks OWNER TO civic_lens_public_owner;

        CREATE VIEW public_api.document_change_word_edits WITH (security_barrier = true) AS
        SELECT d.record_id, d.revision_number, e.hunk_position, e.position, e.before_start, e.before_length, e.after_start, e.after_length
          FROM public_api.document_changes d
          JOIN public.document_comparison_word_edits e ON e.comparison_id = d.comparison_id;
        ALTER VIEW public_api.document_change_word_edits OWNER TO civic_lens_public_owner;

        CREATE VIEW public_api.document_change_citations WITH (security_barrier = true) AS
        SELECT d.record_id, d.revision_number, c.position, c.citation_extraction_id AS extraction_id,
               c.citation_start, c.citation_length, c.is_change_date_evidence
          FROM public_api.document_changes d
          JOIN public.document_change_revision_citations c
            ON c.draft_id = d.record_id AND c.revision_number = d.revision_number;
        ALTER VIEW public_api.document_change_citations OWNER TO civic_lens_public_owner;
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DROP VIEW public_api.document_change_citations;
        DROP VIEW public_api.document_change_word_edits;
        DROP VIEW public_api.document_change_hunks;
        DROP VIEW public_api.document_change_sources;
        DROP VIEW public_api.document_change_officials;
        DROP VIEW public_api.document_changes;
        REVOKE SELECT (comparison_id, summary, author_kind, significance, limits, institution, change_date)
            ON document_change_revisions FROM civic_lens_public_owner;
        REVOKE SELECT (comparison_id, before_extraction_id, after_extraction_id, hunk_count) ON document_comparisons FROM civic_lens_public_owner;
        REVOKE SELECT (comparison_id, position, before_start, before_length, after_start, after_length, before_context_start, before_context_length,
            after_context_start, after_context_length, word_edit_count) ON document_comparison_hunks FROM civic_lens_public_owner;
        REVOKE SELECT (comparison_id, hunk_position, position, before_start, before_length, after_start, after_length)
            ON document_comparison_word_edits FROM civic_lens_public_owner;
        REVOKE SELECT (draft_id, revision_number, position, citation_extraction_id, citation_start, citation_length, is_change_date_evidence)
            ON document_change_revision_citations FROM civic_lens_public_owner;
        REVOKE SELECT (draft_id, revision_number, issue_id, position) ON document_change_revision_issues FROM civic_lens_public_owner;
        REVOKE SELECT (draft_id, revision_number, official_id, position) ON document_change_revision_officials FROM civic_lens_public_owner;
        REVOKE SELECT (final_url) ON collection_attempts FROM civic_lens_public_owner;
        """);
}
