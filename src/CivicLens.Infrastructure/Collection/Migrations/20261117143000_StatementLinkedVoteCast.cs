using CivicLens.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations;

[DbContext(typeof(CollectionAttemptDbContext))]
[Migration("20261117143000_StatementLinkedVoteCast")]
public sealed class StatementLinkedVoteCast : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(WithCitedVote);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(WithoutCitedVote);

    // Append the columns, retain owner and grants, and set the barrier explicitly because replacement resets view options.
    private const string WithCitedVote = """
        CREATE OR REPLACE VIEW public_api.relationships WITH (security_barrier = true) AS
        SELECT a.record_id, a.revision_number, e.headline, e.link_type, e.event_date AS statement_date, r.reason,
               CASE r.author_kind WHEN 'Analysis' THEN 'Model' ELSE 'Person' END AS text_origin,
               CASE WHEN r.author_kind = 'Analysis' THEN 'Drafted by an AI model from these two records. A person checked it before publication.' END AS label,
               e.approved_by AS approver,
               e.statement_record_id, r.statement_revision_number, sh.headline AS statement_headline, sh.text_origin AS statement_text_origin, sh.label AS statement_label,
               e.target_kind, e.target_record_id, coalesce(r.target_action_revision_number, r.target_vote_revision_number) AS target_revision_number,
               th.headline AS target_headline, th.text_origin AS target_text_origin, th.label AS target_label,
               r.statement_quote_extraction_id AS quote_extraction_id, r.statement_quote_start AS quote_start, r.statement_quote_length AS quote_length,
               x.text AS quote_source_text, f.fact, f.fact_label,
               f.fact_text_origin,
               r.official_id AS linked_official_id, cited_vote.vote_cast AS cited_vote_cast, cited_vote.voted_on AS cited_vote_date
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
          LEFT JOIN LATERAL (
              SELECT v.voted_on, m.vote_cast
                FROM public.vote_revisions v
                LEFT JOIN public.vote_member_list_entries m ON m.list_id = v.member_list_id AND m.person_id = r.official_id
               WHERE r.target_kind = 'Vote' AND v.record_id = r.target_vote_record_id AND v.revision_number = r.target_vote_revision_number
          ) cited_vote ON TRUE
          LEFT JOIN LATERAL (SELECT v.fact, v.fact_label, v.fact_text_origin FROM (SELECT 'F' || row_number() OVER (ORDER BY vf."position") AS fact_id, vf.fact_text AS fact, NULL::text AS fact_label, NULL::text AS fact_text_origin
          FROM public.vote_revision_facts vf
         WHERE r.target_kind = 'Vote' AND vf.record_id = r.target_vote_record_id AND vf.revision_number = r.target_vote_revision_number
        UNION ALL
        SELECT c.fact_id, c.fact, c.fact_label, c.fact_text_origin
          FROM public.action_revisions ar
          CROSS JOIN LATERAL (VALUES
              ('F1', 'Bill: ' || ar.bill_type || ' ' || ar.bill_number || ', Congress ' || ar.congress, NULL::text, NULL::text),
              ('F2', 'Title: ' || ar.bill_title, NULL, NULL),
              ('F3', 'Headline: ' || ar.headline, CASE ar.text_origin WHEN 'Model' THEN 'An AI summary of the official record' WHEN 'Code' THEN 'Written automatically from Congress.gov data, with no AI' END, ar.text_origin),
              ('F4', 'Summary: ' || ar.summary, CASE ar.text_origin WHEN 'Model' THEN 'An AI summary of the official record' WHEN 'Code' THEN 'Written automatically from Congress.gov data, with no AI' END, ar.text_origin)) c(fact_id, fact, fact_label, fact_text_origin)
         WHERE r.target_kind = 'Action' AND ar.record_id = r.target_action_record_id AND ar.revision_number = r.target_action_revision_number) v WHERE v.fact_id = r.target_fact_id) f ON TRUE
         WHERE a.kind = 'relationship' AND e.statement_kind = 'statement';
        """;

    private const string WithoutCitedVote = """
        DROP VIEW public_api.relationships;
        CREATE VIEW public_api.relationships WITH (security_barrier = true) AS
        SELECT a.record_id, a.revision_number, e.headline, e.link_type, e.event_date AS statement_date, r.reason,
               CASE r.author_kind WHEN 'Analysis' THEN 'Model' ELSE 'Person' END AS text_origin,
               CASE WHEN r.author_kind = 'Analysis' THEN 'Drafted by an AI model from these two records. A person checked it before publication.' END AS label,
               e.approved_by AS approver,
               e.statement_record_id, r.statement_revision_number, sh.headline AS statement_headline, sh.text_origin AS statement_text_origin, sh.label AS statement_label,
               e.target_kind, e.target_record_id, coalesce(r.target_action_revision_number, r.target_vote_revision_number) AS target_revision_number,
               th.headline AS target_headline, th.text_origin AS target_text_origin, th.label AS target_label,
               r.statement_quote_extraction_id AS quote_extraction_id, r.statement_quote_start AS quote_start, r.statement_quote_length AS quote_length,
               x.text AS quote_source_text, f.fact, f.fact_label,
               f.fact_text_origin
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
          LEFT JOIN LATERAL (SELECT v.fact, v.fact_label, v.fact_text_origin FROM (SELECT 'F' || row_number() OVER (ORDER BY vf."position") AS fact_id, vf.fact_text AS fact, NULL::text AS fact_label, NULL::text AS fact_text_origin
          FROM public.vote_revision_facts vf
         WHERE r.target_kind = 'Vote' AND vf.record_id = r.target_vote_record_id AND vf.revision_number = r.target_vote_revision_number
        UNION ALL
        SELECT c.fact_id, c.fact, c.fact_label, c.fact_text_origin
          FROM public.action_revisions ar
          CROSS JOIN LATERAL (VALUES
              ('F1', 'Bill: ' || ar.bill_type || ' ' || ar.bill_number || ', Congress ' || ar.congress, NULL::text, NULL::text),
              ('F2', 'Title: ' || ar.bill_title, NULL, NULL),
              ('F3', 'Headline: ' || ar.headline, CASE ar.text_origin WHEN 'Model' THEN 'An AI summary of the official record' WHEN 'Code' THEN 'Written automatically from Congress.gov data, with no AI' END, ar.text_origin),
              ('F4', 'Summary: ' || ar.summary, CASE ar.text_origin WHEN 'Model' THEN 'An AI summary of the official record' WHEN 'Code' THEN 'Written automatically from Congress.gov data, with no AI' END, ar.text_origin)) c(fact_id, fact, fact_label, fact_text_origin)
         WHERE r.target_kind = 'Action' AND ar.record_id = r.target_action_record_id AND ar.revision_number = r.target_action_revision_number) v WHERE v.fact_id = r.target_fact_id) f ON TRUE
         WHERE a.kind = 'relationship' AND e.statement_kind = 'statement';
        ALTER VIEW public_api.relationships OWNER TO civic_lens_public_owner;
        GRANT SELECT ON public_api.relationships TO civic_lens_public;
        REVOKE SELECT (official_id) ON relationship_revisions FROM civic_lens_public_owner;
        """;
}
