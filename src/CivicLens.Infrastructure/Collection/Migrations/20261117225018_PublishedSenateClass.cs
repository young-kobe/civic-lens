using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations;

/// <inheritdoc />
public partial class PublishedSenateClass : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "senate_class",
            table: "release_official_terms",
            type: "integer",
            nullable: true);

        migrationBuilder.AddCheckConstraint(
            name: "ck_release_official_terms_senate_class",
            table: "release_official_terms",
            sql: "senate_class IS NULL OR (kind = 'Senator' AND senate_class BETWEEN 1 AND 3)");

        migrationBuilder.Sql(WithSenateClass);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(WithoutSenateClass);

        migrationBuilder.DropCheckConstraint(
            name: "ck_release_official_terms_senate_class",
            table: "release_official_terms");

        migrationBuilder.DropColumn(
            name: "senate_class",
            table: "release_official_terms");
    }

    private const string WithSenateClass = """
    GRANT SELECT (senate_class) ON public.release_official_terms TO civic_lens_public_owner;
    CREATE OR REPLACE VIEW public_api.release_official_terms WITH (security_barrier = true) AS
    SELECT t.release_number, t.official_id, t."position", t.office, t.kind, t.state, t.district, t.party, t.caucus, t.start_on, t.end_on, t.senate_class
      FROM public.release_official_terms t JOIN public.publication_releases r ON r.release_number = t.release_number
     WHERE r.is_active;
    """;

    private const string WithoutSenateClass = """
    DROP VIEW public_api.bill_sponsorship_parties, public_api.statement_speaker_terms;
    DROP VIEW public_api.release_official_terms;
    CREATE VIEW public_api.release_official_terms WITH (security_barrier = true) AS
    SELECT t.release_number, t.official_id, t."position", t.office, t.kind, t.state, t.district, t.party, t.caucus, t.start_on, t.end_on
      FROM public.release_official_terms t JOIN public.publication_releases r ON r.release_number = t.release_number
     WHERE r.is_active;
    CREATE VIEW public_api.bill_sponsorship_parties WITH (security_barrier = true) AS
    SELECT s.record_id, s.revision_number, s."position", term.party
      FROM public_api.bill_sponsorships s
    CROSS JOIN LATERAL (
           SELECT CASE WHEN count(DISTINCT held.party) = 1 AND count(held.party) = count(*) THEN min(held.party) END AS party
             FROM (SELECT CASE WHEN btrim(t.party) <> '' THEN t.party END AS party
                     FROM public_api.release_official_terms t
                    WHERE t.official_id = s.official_id
                      AND t.start_on <= s.sponsored_on AND (t.end_on IS NULL OR t.end_on >= s.sponsored_on)) held) term;
    CREATE VIEW public_api.statement_speaker_terms WITH (security_barrier = true) AS
    SELECT p.record_id, p.revision_number, p."position", term.party, seat.kind, seat.office, seat.state, seat.district
      FROM public_api.statement_speakers p
      JOIN public_api.statements s ON s.record_id = p.record_id AND s.revision_number = p.revision_number
    CROSS JOIN LATERAL (
           SELECT CASE WHEN count(DISTINCT held.party) = 1 AND count(held.party) = count(*) THEN min(held.party) END AS party
             FROM (SELECT CASE WHEN btrim(t.party) <> '' THEN t.party END AS party
                     FROM public_api.release_official_terms t
                    WHERE t.official_id = p.official_id
                      AND t.start_on <= s.statement_date AND (t.end_on IS NULL OR t.end_on >= s.statement_date)) held) term
      LEFT JOIN LATERAL (
           SELECT min(t.kind) AS kind, min(t.office) AS office, min(t.state) AS state, min(t.district) AS district
             FROM public_api.release_official_terms t
            WHERE t.official_id = p.official_id
              AND t.start_on <= s.statement_date AND (t.end_on IS NULL OR t.end_on >= s.statement_date)
           HAVING count(*) = 1) seat ON TRUE;
    ALTER VIEW public_api.release_official_terms OWNER TO civic_lens_public_owner;
    ALTER VIEW public_api.bill_sponsorship_parties OWNER TO civic_lens_public_owner;
    ALTER VIEW public_api.statement_speaker_terms OWNER TO civic_lens_public_owner;
    GRANT SELECT ON public_api.release_official_terms, public_api.bill_sponsorship_parties,
        public_api.statement_speaker_terms TO civic_lens_public;
    """;
}
