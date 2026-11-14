using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations;

/// <summary>
/// Adds <c>public_api.official_vote_parties</c>: one row for each official, chamber, Congress, and pinned party of the active votes.
/// It reads <c>member_votes</c> once, so the boards read every vote figure in one pass. The view owns the attendance and party-line counts:
/// <c>official_attendance</c> and <c>official_party_lines</c> become projections of it, so each count keeps one owner.
/// The party and the party-break rule stay with <c>member_votes</c>. The rollback puts back the two old views and drops the new one.
/// </summary>
public partial class PublicApiOfficialVoteParties : Migration
{
    // held, missed, and the vote days count every party row of the official in the chamber and Congress, as official_attendance did.
    // The party-line columns count the rows of the one pinned party where member_votes.breaks_party is not null, as official_party_lines did.
    // unwritten_votes counts the rows of a vote with no stored alignment, so a reader can tell an incomplete Congress from a zero.
    private const string UpSql = """
        CREATE VIEW public_api.official_vote_parties WITH (security_barrier = true) AS
        SELECT g.official_id, g.chamber, g.congress, g.party,
               (sum(g.party_held) OVER seat)::bigint AS held,
               (sum(g.party_missed) OVER seat)::bigint AS missed,
               min(g.party_first_voted_on) OVER seat AS first_voted_on,
               max(g.party_last_voted_on) OVER seat AS last_voted_on,
               g.with_party, g.against_party, g.cast_count, g.first_cast_on, g.last_cast_on, g.unwritten_votes
          FROM (
              SELECT m.official_id, m.chamber, m.congress, m.party,
                     count(*) AS party_held,
                     count(*) FILTER (WHERE m.vote_cast = 'Not Voting') AS party_missed,
                     min(m.voted_on) AS party_first_voted_on, max(m.voted_on) AS party_last_voted_on,
                     count(*) FILTER (WHERE NOT m.breaks_party) AS with_party,
                     count(*) FILTER (WHERE m.breaks_party) AS against_party,
                     count(*) FILTER (WHERE m.breaks_party IS NOT NULL) AS cast_count,
                     min(m.voted_on) FILTER (WHERE m.breaks_party IS NOT NULL) AS first_cast_on,
                     max(m.voted_on) FILTER (WHERE m.breaks_party IS NOT NULL) AS last_cast_on,
                     count(*) FILTER (WHERE NOT m.alignment_known) AS unwritten_votes
                FROM public_api.member_votes m
               GROUP BY m.official_id, m.chamber, m.congress, m.party) g
        WINDOW seat AS (PARTITION BY g.official_id, g.chamber, g.congress);

        ALTER VIEW public_api.official_vote_parties OWNER TO civic_lens_public_owner;

        -- CREATE OR REPLACE keeps the owner and the grant of each old view. db migrate grants the new view to the public role.
        CREATE OR REPLACE VIEW public_api.official_attendance WITH (security_barrier = true) AS
        SELECT DISTINCT p.official_id, p.chamber, p.congress, p.held, p.missed, p.first_voted_on, p.last_voted_on
          FROM public_api.official_vote_parties p;

        CREATE OR REPLACE VIEW public_api.official_party_lines WITH (security_barrier = true) AS
        SELECT p.official_id, p.chamber, p.congress, p.party, p.with_party, p.against_party, p.cast_count,
               p.first_cast_on AS first_voted_on, p.last_cast_on AS last_voted_on
          FROM public_api.official_vote_parties p
         WHERE p.cast_count > 0;
        """;

    private static readonly string DownSql = $"""
        CREATE OR REPLACE VIEW public_api.official_attendance WITH (security_barrier = true) AS
        SELECT e.person_id AS official_id, v.chamber, v.congress,
               count(*) AS held, count(*) FILTER (WHERE e.vote_cast = 'Not Voting') AS missed,
               min(v.voted_on) AS first_voted_on, max(v.voted_on) AS last_voted_on
          FROM public_api.active_records a
          JOIN public.vote_revisions v ON v.record_id = a.record_id AND v.revision_number = a.revision_number
          JOIN public.vote_member_list_entries e ON e.list_id = v.member_list_id
         WHERE a.kind = 'vote'
         GROUP BY e.person_id, v.chamber, v.congress;

        {PublicApiMemberVoteBreaksParty.PartyLinesSql}

        DROP VIEW public_api.official_vote_parties;
        """;

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(UpSql);

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(DownSql);
}
