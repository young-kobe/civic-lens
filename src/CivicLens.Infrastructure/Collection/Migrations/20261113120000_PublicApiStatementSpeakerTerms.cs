using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations;

/// <summary>
/// Adds the view <c>public_api.statement_speaker_terms</c>: the term party and the seat of each speaker on the statement date. <see cref="TermPartyOn"/>
/// is the one SQL owner of the term party rule, so the migration also rebuilds <c>public_api.bill_sponsorship_parties</c> from it with the same
/// columns and rows. A day that no term holds, a blank term party, and terms of two parties on one day give no party. The seat comes from the one
/// term that holds the day, so a day that no term or two terms hold gives no seat. An undated statement has no day, so it has no party and no seat.
/// The rollback drops the new view. The rebuilt party view keeps the rule of <c>PublicApiBillSponsorPartiesAndTimes</c>, so it stays.
/// </summary>
public partial class PublicApiStatementSpeakerTerms : Migration
{
    /// <summary>The published terms that the public views read.</summary>
    private const string PublicTerms = "public_api.release_official_terms";

    /// <summary>The lateral subquery that gives the column <c>term.party</c>: the party that every term of the official held on the day, or null.
    /// <paramref name="terms"/> is the relation of the terms to read: the public view, or the terms of one release in the release commit.</summary>
    internal static string TermPartyOn(string terms, string officialColumn, string dayColumn) => $"""
        CROSS JOIN LATERAL (
               SELECT CASE WHEN count(DISTINCT held.party) = 1 AND count(held.party) = count(*) THEN min(held.party) END AS party
                 FROM (SELECT CASE WHEN btrim(t.party) <> '' THEN t.party END AS party
                         FROM {terms} t
                        WHERE t.official_id = {officialColumn}
                          AND t.start_on <= {dayColumn} AND (t.end_on IS NULL OR t.end_on >= {dayColumn})) held) term
        """;

    private static readonly string UpSql = $"""
        CREATE OR REPLACE VIEW public_api.bill_sponsorship_parties WITH (security_barrier = true) AS
        SELECT s.record_id, s.revision_number, s."position", term.party
          FROM public_api.bill_sponsorships s
        {TermPartyOn(PublicTerms, "s.official_id", "s.sponsored_on")};

        CREATE VIEW public_api.statement_speaker_terms WITH (security_barrier = true) AS
        SELECT p.record_id, p.revision_number, p."position", term.party, seat.kind, seat.office, seat.state, seat.district
          FROM public_api.statement_speakers p
          JOIN public_api.statements s ON s.record_id = p.record_id AND s.revision_number = p.revision_number
        {TermPartyOn(PublicTerms, "p.official_id", "s.statement_date")}
          LEFT JOIN LATERAL (
               SELECT min(t.kind) AS kind, min(t.office) AS office, min(t.state) AS state, min(t.district) AS district
                 FROM public_api.release_official_terms t
                WHERE t.official_id = p.official_id
                  AND t.start_on <= s.statement_date AND (t.end_on IS NULL OR t.end_on >= s.statement_date)
               HAVING count(*) = 1) seat ON TRUE;
        ALTER VIEW public_api.statement_speaker_terms OWNER TO civic_lens_public_owner;
        DO $$
        BEGIN
          IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'civic_lens_public') THEN
            GRANT SELECT ON public_api.statement_speaker_terms TO civic_lens_public;
          END IF;
        END $$;
        """;

    private const string DownSql = "DROP VIEW public_api.statement_speaker_terms;";

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(UpSql);

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(DownSql);
}
