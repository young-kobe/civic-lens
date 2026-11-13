using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations;

/// <summary>
/// Exposes the cited evidence of the active executive action revisions for the server page. <c>public_api.executive_action_facts</c> holds the span of
/// each cited fact in the order of <c>PublishedExecutiveAction.Facts</c>: the fact rows, then the disposition notes, then the correction notices.
/// <c>public_api.executive_action_summary_sentences</c> gains the quote span of each sentence. <c>public_api.executive_action_summary_sources</c> holds
/// the one body text extraction that the quotes of a summary cite. The page reads the quotes from it and never shows the whole text.
/// </summary>
public partial class PublicApiExecutiveActionEvidence : Migration
{
    // ExecutiveActionRevision.Citations: the facts, then the disposition notes, then the correction notices. PublishedExecutiveAction.Facts keeps that order.
    private const string FactsSql = """
        CREATE VIEW public_api.executive_action_facts WITH (security_barrier = true) AS
        SELECT e.record_id, e.revision_number,
               (row_number() OVER (PARTITION BY e.record_id, e.revision_number ORDER BY c.rank) - 1)::integer AS "position",
               c.extraction_id, c.citation_start, c.citation_length
          FROM public_api.executive_actions e
          JOIN public.executive_action_revisions r ON r.record_id = e.record_id AND r.revision_number = e.revision_number
          CROSS JOIN LATERAL (
              SELECT f.citation_extraction_id AS extraction_id, f.citation_start, f.citation_length, ARRAY[0, f."position"] AS rank
                FROM public.executive_action_revision_facts f
               WHERE f.record_id = r.record_id AND f.revision_number = r.revision_number
              UNION ALL
              SELECT r.disposition_notes_extraction_id, r.disposition_notes_start, r.disposition_notes_length, ARRAY[1, 0]
               WHERE r.disposition_notes_extraction_id IS NOT NULL
              UNION ALL
              SELECT d.notice_citation_extraction_id, d.notice_citation_start, d.notice_citation_length, ARRAY[2, n."position"]
                FROM public.executive_action_revision_notices n
                JOIN public.executive_action_documents d ON d.source_id = n.source_id AND d.document_number = n.document_number
               WHERE n.record_id = r.record_id AND n.revision_number = r.revision_number) c;
        ALTER VIEW public_api.executive_action_facts OWNER TO civic_lens_public_owner;
        """;

    private const string OldSentencesSql = """
        CREATE VIEW public_api.executive_action_summary_sentences WITH (security_barrier = true) AS
        SELECT s.record_id, s.revision_number, s."position", s.sentence
          FROM public_api.executive_action_summaries m
          JOIN public.executive_action_revision_summary_sentences s ON s.record_id = m.record_id AND s.revision_number = m.revision_number
         WHERE m.text_origin = 'Model';
        ALTER VIEW public_api.executive_action_summary_sentences OWNER TO civic_lens_public_owner;
        """;

    // The new columns follow the old ones, so CREATE OR REPLACE keeps the view and its grant.
    private const string SentencesSql = """
        CREATE OR REPLACE VIEW public_api.executive_action_summary_sentences WITH (security_barrier = true) AS
        SELECT s.record_id, s.revision_number, s."position", s.sentence, s.quote_extraction_id AS extraction_id, s.quote_start, s.quote_length
          FROM public_api.executive_action_summaries m
          JOIN public.executive_action_revision_summary_sentences s ON s.record_id = m.record_id AND s.revision_number = m.revision_number
         WHERE m.text_origin = 'Model';
        """;

    private const string SummarySourcesSql = """
        CREATE VIEW public_api.executive_action_summary_sources WITH (security_barrier = true) AS
        SELECT q.record_id, q.revision_number, q.extraction_id, attempt.final_url AS url, attempt.observed_at_utc_ticks, extraction.text
          FROM (SELECT DISTINCT s.record_id, s.revision_number, s.extraction_id FROM public_api.executive_action_summary_sentences s) q
          LEFT JOIN public.document_extractions extraction ON extraction.extraction_id = q.extraction_id
          LEFT JOIN public.collection_attempts attempt ON attempt.attempt_id = extraction.attempt_id;
        ALTER VIEW public_api.executive_action_summary_sources OWNER TO civic_lens_public_owner;
        """;

    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(string.Join('\n', FactsSql, SentencesSql, SummarySourcesSql));

    // A dropped view loses its grant, so the rollback grants the rebuilt sentence view again. It revokes only the base columns that no older view reads.
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql($"""
        DROP VIEW public_api.executive_action_summary_sources;
        DROP VIEW public_api.executive_action_summary_sentences;
        DROP VIEW public_api.executive_action_facts;
        {OldSentencesSql}
        GRANT SELECT ON public_api.executive_action_summary_sentences TO civic_lens_public;
        REVOKE SELECT (citation_start, citation_length) ON executive_action_revision_facts FROM civic_lens_public_owner;
        REVOKE SELECT (disposition_notes_start, disposition_notes_length) ON executive_action_revisions FROM civic_lens_public_owner;
        REVOKE SELECT (notice_citation_start, notice_citation_length) ON executive_action_documents FROM civic_lens_public_owner;
        REVOKE SELECT (quote_extraction_id, quote_start, quote_length) ON executive_action_revision_summary_sentences FROM civic_lens_public_owner;
        """);
}
