using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations;

/// <summary>Adds the state and the district of the term to <c>public_api.vote_members</c>. The term LATERAL is the one that already gives the caucus.</summary>
public partial class PublicApiVoteMemberDistricts : Migration
{
    private const string MembersFromSql = """
          FROM public_api.active_records a
          JOIN public.vote_revisions v ON v.record_id = a.record_id AND v.revision_number = a.revision_number
          JOIN public.vote_member_list_entries e ON e.list_id = v.member_list_id
          LEFT JOIN LATERAL (
               SELECT x.caucus, x.state, x.district
                 FROM public.release_official_terms x
                WHERE x.release_number = a.release_number AND x.official_id = e.person_id
                  AND x.start_on <= v.voted_on AND (x.end_on IS NULL OR x.end_on >= v.voted_on)
                  AND ((v.chamber = 'House' AND x.kind = 'Representative') OR (v.chamber = 'Senate' AND x.kind = 'Senator'))
                ORDER BY x.start_on DESC, x."position" DESC LIMIT 1) term ON TRUE
         WHERE a.kind = 'vote'
        """;

    private const string MemberColumns = """
        a.record_id, a.revision_number, e."position", e.person_id AS official_id, e.vote_cast,
        e.party, term.caucus, e.citation_extraction_id AS extraction_id, e.citation_start, e.citation_length
        """;

    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql($"""
        CREATE OR REPLACE VIEW public_api.vote_members WITH (security_barrier = true) AS
        SELECT {MemberColumns}, term.state, term.district
        {MembersFromSql};
        ALTER VIEW public_api.vote_members OWNER TO civic_lens_public_owner;
        """);

    // A view cannot lose a column by CREATE OR REPLACE, so the old shape is dropped and made again. vote_sources reads vote_members.
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql($"""
        DROP VIEW public_api.vote_sources;
        DROP VIEW public_api.vote_members;

        CREATE VIEW public_api.vote_members WITH (security_barrier = true) AS
        SELECT {MemberColumns}
        {MembersFromSql};
        ALTER VIEW public_api.vote_members OWNER TO civic_lens_public_owner;

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
        """);
}
