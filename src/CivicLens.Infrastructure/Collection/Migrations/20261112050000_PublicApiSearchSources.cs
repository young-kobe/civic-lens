using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations;

/// <summary>Exposes the ordered sources of the active action, vote, and executive action revisions and the quote span of each relationship.
/// The search ingest will read them instead of the record files. The source order is the order of <c>PublishedRecordRules.Sources</c>.</summary>
public partial class PublicApiSearchSources : Migration
{
    private const string SourceColumns = """
        cited.extraction_id, attempt.final_url AS url, attempt.observed_at_utc_ticks, extraction.text
        """;

    private const string SourceJoins = """
          LEFT JOIN public.document_extractions extraction ON extraction.extraction_id = cited.extraction_id
          LEFT JOIN public.collection_attempts attempt ON attempt.attempt_id = extraction.attempt_id
        """;

    internal const string LinkColumns = """
        l.link_type, l.event_date AS statement_date, l.record_id AS link_record_id, r.reason,
               r.statement_quote_extraction_id AS quote_extraction_id, r.statement_quote_start AS quote_start, r.statement_quote_length AS quote_length
        """;

    internal const string LinkJoins = """
          FROM public.published_entries l
          JOIN public_api.active_records a ON a.kind = 'relationship' AND a.record_id = l.record_id AND a.revision_number = l.revision_number
          JOIN public_api.active_records s ON s.kind = l.statement_kind AND s.record_id = l.statement_record_id
          JOIN public_api.active_records t ON t.kind = l.target_kind AND t.record_id = l.target_record_id
          LEFT JOIN public.relationship_revisions r ON r.record_id = a.record_id AND r.revision_number = a.revision_number
        """;

    // A citation list gives each row an extraction ID and a rank array in citation order.
    // The position of a source is the rank of its first citation, as PublishedRecordRules.Sources keeps the first appearance.
    private static string FirstAppearance(string citations) => $"""
        CROSS JOIN LATERAL (
              SELECT c.extraction_id, (row_number() OVER (ORDER BY min(c.rank)) - 1)::integer AS "position"
                FROM ({citations}) c
               GROUP BY c.extraction_id) cited
        """;

    // ActionDraftRevision.Citations: sponsorship citations, then the summary, policy area, and bill citations, then the text citations.
    private const string ActionCitations = """
        SELECT p.citation_extraction_id AS extraction_id, ARRAY[0, p.sponsorship_position, p."position"] AS rank
          FROM public.action_revision_sponsorship_citations p
         WHERE p.record_id = r.record_id AND p.revision_number = r.revision_number
        UNION ALL
        SELECT r.summary_citation_extraction_id, ARRAY[1, 0, 0] WHERE r.summary_citation_extraction_id IS NOT NULL
        UNION ALL
        SELECT r.policy_area_citation_extraction_id, ARRAY[2, 0, 0] WHERE r.policy_area_citation_extraction_id IS NOT NULL
        UNION ALL
        SELECT r.bill_citation_extraction_id, ARRAY[3, 0, 0] WHERE r.bill_citation_extraction_id IS NOT NULL
        UNION ALL
        SELECT x.citation_extraction_id, ARRAY[4, x."position", 0]
          FROM public.action_revision_text_citations x
         WHERE x.record_id = r.record_id AND x.revision_number = r.revision_number
        """;

    // ExecutiveActionRevision.Citations: the facts, then the disposition notes, then the correction notices.
    private const string ExecutiveActionCitations = """
        SELECT f.citation_extraction_id AS extraction_id, ARRAY[0, f."position"] AS rank
          FROM public.executive_action_revision_facts f
         WHERE f.record_id = r.record_id AND f.revision_number = r.revision_number
        UNION ALL
        SELECT r.disposition_notes_extraction_id, ARRAY[1, 0] WHERE r.disposition_notes_extraction_id IS NOT NULL
        UNION ALL
        SELECT d.notice_citation_extraction_id, ARRAY[2, n."position"]
          FROM public.executive_action_revision_notices n
          JOIN public.executive_action_documents d ON d.source_id = n.source_id AND d.document_number = n.document_number
         WHERE n.record_id = r.record_id AND n.revision_number = r.revision_number
        """;

    // VoteDraftRevision.Citations: the date citation, then the facts, then the kind citation, then the members.
    private const string VoteCitations = """
        SELECT v.voted_on_citation_extraction_id AS extraction_id, ARRAY[0, 0] AS rank
        UNION ALL
        SELECT f.extraction_id, ARRAY[1, f."position"] FROM public_api.vote_facts f
         WHERE f.record_id = v.record_id AND f.revision_number = v.revision_number
        UNION ALL
        SELECT v.kind_citation_extraction_id, ARRAY[2, 0] WHERE v.kind_citation_extraction_id IS NOT NULL
        UNION ALL
        SELECT m.extraction_id, ARRAY[3, m."position"] FROM public_api.vote_members m
         WHERE m.record_id = v.record_id AND m.revision_number = v.revision_number
        """;

    internal static readonly string ActionSources = $"""
        CREATE VIEW public_api.action_sources WITH (security_barrier = true) AS
        SELECT b.record_id, b.revision_number, cited."position", {SourceColumns}
          FROM public_api.bills b
          JOIN public.action_revisions r ON r.record_id = b.record_id AND r.revision_number = b.revision_number
          {FirstAppearance(ActionCitations)}
        {SourceJoins};
        ALTER VIEW public_api.action_sources OWNER TO civic_lens_public_owner;
        """;

    private static readonly string ExecutiveActionSources = $"""
        CREATE VIEW public_api.executive_action_sources WITH (security_barrier = true) AS
        SELECT e.record_id, e.revision_number, cited."position", {SourceColumns}
          FROM public_api.executive_actions e
          JOIN public.executive_action_revisions r ON r.record_id = e.record_id AND r.revision_number = e.revision_number
          {FirstAppearance(ExecutiveActionCitations)}
        {SourceJoins};
        ALTER VIEW public_api.executive_action_sources OWNER TO civic_lens_public_owner;
        """;

    private static readonly string VoteSources = $"""
        CREATE OR REPLACE VIEW public_api.vote_sources WITH (security_barrier = true) AS
        SELECT v.record_id, v.revision_number, {SourceColumns}, cited."position"
          FROM public_api.vote_evidence v
          {FirstAppearance(VoteCitations)}
        {SourceJoins};
        ALTER VIEW public_api.vote_sources OWNER TO civic_lens_public_owner;
        """;

    private const string OldVoteSources = """
        CREATE VIEW public_api.vote_sources WITH (security_barrier = true) AS
        SELECT v.record_id, v.revision_number, cited.extraction_id,
               attempt.final_url AS url, attempt.observed_at_utc_ticks, extraction.text
          FROM public_api.vote_evidence v
          CROSS JOIN LATERAL (
              SELECT v.voted_on_citation_extraction_id AS extraction_id
              UNION
              SELECT v.kind_citation_extraction_id WHERE v.kind_citation_extraction_id IS NOT NULL
              UNION
              SELECT f.extraction_id FROM public_api.vote_facts f
               WHERE f.record_id = v.record_id AND f.revision_number = v.revision_number
              UNION
              SELECT m.extraction_id FROM public_api.vote_members m
               WHERE m.record_id = v.record_id AND m.revision_number = v.revision_number) cited
          LEFT JOIN public.document_extractions extraction ON extraction.extraction_id = cited.extraction_id
          LEFT JOIN public.collection_attempts attempt ON attempt.attempt_id = extraction.attempt_id;
        ALTER VIEW public_api.vote_sources OWNER TO civic_lens_public_owner;
        """;

    internal const string RecordLinks = $"""
        CREATE OR REPLACE VIEW public_api.record_links WITH (security_barrier = true) AS
        SELECT l.statement_record_id AS record_id, 'statement' AS kind,
               t.kind AS related_kind, t.record_id AS related_record_id, h.headline AS related_headline, h.text_origin AS related_text_origin, h.label AS related_label,
               {LinkColumns}
        {LinkJoins}
          CROSS JOIN LATERAL (SELECT x.headline, x.text_origin, x.label FROM public_api.record_headlines x WHERE x.kind = t.kind AND x.record_id = t.record_id OFFSET 0) h
         WHERE l.kind = 'relationship' AND l.statement_kind = 'statement'
        UNION ALL
        SELECT l.target_record_id, l.target_kind,
               'statement', s.record_id, h.headline, h.text_origin, h.label,
               {LinkColumns}
        {LinkJoins}
          CROSS JOIN LATERAL (SELECT x.headline, x.text_origin, x.label FROM public_api.record_headlines x WHERE x.kind = l.statement_kind AND x.record_id = l.statement_record_id OFFSET 0) h
         WHERE l.kind = 'relationship' AND l.statement_kind = 'statement';
        ALTER VIEW public_api.record_links OWNER TO civic_lens_public_owner;
        """;

    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(string.Join('\n', ActionSources, ExecutiveActionSources, VoteSources, RecordLinks));

    // A dropped view loses its grants, so the rollback grants the rebuilt views again. It revokes only the base columns that no older view reads.
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql($"""
        DROP VIEW public_api.action_sources;
        DROP VIEW public_api.executive_action_sources;
        DROP VIEW public_api.vote_sources;
        DROP VIEW public_api.record_links;
        {OldVoteSources}
        {PublicApiRecordLinkReason.UpSql}
        GRANT SELECT ON public_api.vote_sources, public_api.record_links TO civic_lens_public;

        REVOKE SELECT (summary_citation_extraction_id, policy_area_citation_extraction_id, bill_citation_extraction_id)
            ON action_revisions FROM civic_lens_public_owner;
        REVOKE SELECT (record_id, revision_number, sponsorship_position, "position", citation_extraction_id)
            ON action_revision_sponsorship_citations FROM civic_lens_public_owner;
        REVOKE SELECT (record_id, revision_number, "position", citation_extraction_id) ON action_revision_text_citations FROM civic_lens_public_owner;
        REVOKE SELECT (record_id, revision_number, "position", citation_extraction_id) ON executive_action_revision_facts FROM civic_lens_public_owner;
        REVOKE SELECT (disposition_notes_extraction_id) ON executive_action_revisions FROM civic_lens_public_owner;
        REVOKE SELECT (notice_citation_extraction_id) ON executive_action_documents FROM civic_lens_public_owner;
        REVOKE SELECT (statement_quote_extraction_id, statement_quote_start, statement_quote_length) ON relationship_revisions FROM civic_lens_public_owner;
        """);
}
