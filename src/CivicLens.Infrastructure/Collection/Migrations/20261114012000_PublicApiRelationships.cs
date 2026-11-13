using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations;

/// <summary>
/// Exposes each active relationship for the server page: its reason with the text origin and AI label, the cited revision of each end with the
/// current headline and label of that end, the quote span with the text of the extraction that it cites, and the cited fact of the target.
/// The record page cuts the quote in C#, because the span counts UTF-16 code units and SQL counts code points. A relationship shows only when both
/// ends are active, as in <c>public_api.record_links</c>.
/// </summary>
public partial class PublicApiRelationships : Migration
{
    // RelationshipTarget.Of owns the fact words: F1 to F4 of an action and one numbered fact per vote fact in position order. A view test compares them.
    // The headline and summary facts of an action are its model or code text, so they carry the label of record_headlines.
    private const string FactsSql = $"""
        SELECT 'F' || row_number() OVER (ORDER BY vf."position") AS fact_id, vf.fact_text AS fact, NULL::text AS fact_label
          FROM public.vote_revision_facts vf
         WHERE r.target_kind = 'Vote' AND vf.record_id = r.target_vote_record_id AND vf.revision_number = r.target_vote_revision_number
        UNION ALL
        SELECT c.fact_id, c.fact, c.fact_label
          FROM public.action_revisions ar
          CROSS JOIN LATERAL (VALUES
              ('F1', 'Bill: ' || ar.bill_type || ' ' || ar.bill_number || ', Congress ' || ar.congress, NULL::text),
              ('F2', 'Title: ' || ar.bill_title, NULL),
              ('F3', 'Headline: ' || ar.headline, {ActionTextLabel}),
              ('F4', 'Summary: ' || ar.summary, {ActionTextLabel})) c(fact_id, fact, fact_label)
         WHERE r.target_kind = 'Action' AND ar.record_id = r.target_action_record_id AND ar.revision_number = r.target_action_revision_number
        """;

    private const string ActionTextLabel = """
        CASE ar.text_origin WHEN 'Model' THEN 'An AI summary of the official record' WHEN 'Code' THEN 'Written automatically from Congress.gov data, with no AI' END
        """;

    // People approve every relationship. The label of a reason that a model drafted repeats PublishedWording.ModelRelationship, and a test compares the two.
    private const string RelationshipsSql = $"""
        CREATE VIEW public_api.relationships WITH (security_barrier = true) AS
        SELECT a.record_id, a.revision_number, e.headline, e.link_type, e.event_date AS statement_date, r.reason,
               CASE r.author_kind WHEN 'Analysis' THEN 'Model' ELSE 'Person' END AS text_origin,
               CASE WHEN r.author_kind = 'Analysis' THEN 'Drafted by an AI model from these two records. A person checked it before publication.' END AS label,
               e.approved_by AS approver,
               e.statement_record_id, r.statement_revision_number, sh.headline AS statement_headline, sh.text_origin AS statement_text_origin, sh.label AS statement_label,
               e.target_kind, e.target_record_id, coalesce(r.target_action_revision_number, r.target_vote_revision_number) AS target_revision_number,
               th.headline AS target_headline, th.text_origin AS target_text_origin, th.label AS target_label,
               r.statement_quote_extraction_id AS quote_extraction_id, r.statement_quote_start AS quote_start, r.statement_quote_length AS quote_length,
               x.text AS quote_source_text, f.fact, f.fact_label
          FROM public_api.active_records a
          JOIN public.published_entries e ON e.kind = 'relationship' AND e.record_id = a.record_id AND e.revision_number = a.revision_number
          JOIN public_api.active_records s ON s.kind = e.statement_kind AND s.record_id = e.statement_record_id
          JOIN public_api.active_records t ON t.kind = e.target_kind AND t.record_id = e.target_record_id
          JOIN public.relationship_revisions r ON r.record_id = a.record_id AND r.revision_number = a.revision_number
          LEFT JOIN public.document_extractions x ON x.extraction_id = r.statement_quote_extraction_id
          LEFT JOIN LATERAL (SELECT h.headline, h.text_origin, h.label FROM public_api.record_headlines h
                              WHERE h.kind = e.statement_kind AND h.record_id = e.statement_record_id OFFSET 0) sh ON TRUE
          LEFT JOIN LATERAL (SELECT h.headline, h.text_origin, h.label FROM public_api.record_headlines h
                              WHERE h.kind = e.target_kind AND h.record_id = e.target_record_id OFFSET 0) th ON TRUE
          LEFT JOIN LATERAL (SELECT v.fact, v.fact_label FROM ({FactsSql}) v WHERE v.fact_id = r.target_fact_id) f ON TRUE
         WHERE a.kind = 'relationship' AND e.statement_kind = 'statement';
        ALTER VIEW public_api.relationships OWNER TO civic_lens_public_owner;
        """;

    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(RelationshipsSql);

    // The rollback revokes only the base columns that no older view reads.
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DROP VIEW public_api.relationships;
        REVOKE SELECT (author_kind, statement_revision_number, target_kind, target_action_record_id, target_action_revision_number,
            target_vote_record_id, target_vote_revision_number, target_fact_id) ON relationship_revisions FROM civic_lens_public_owner;
        """);
}
