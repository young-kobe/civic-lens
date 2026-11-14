using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations;

/// <summary>
/// Gives the rule "this vote broke from most of the member's party" one owner: the column <c>breaks_party</c> of <c>public_api.member_votes</c>.
/// The view <c>official_party_lines</c> and the official page vote strip both read it.
/// </summary>
public partial class PublicApiMemberVoteBreaksParty : Migration
{
    internal const string MemberVotesColumns = """
        SELECT e.person_id AS official_id, a.record_id,
               v.chamber, v.congress, v.session_number, v.roll_call_number, v.voted_on, v.kind, v.bill_type, v.bill_number,
               v.headline,
               CASE v.text_origin
                 WHEN 'Model' THEN 'An AI summary of the official record'
                 WHEN 'Code' THEN 'Written automatically from Congress.gov data, with no AI'
               END AS label,
               v.result, e.vote_cast,
               ARRAY(SELECT i.issue_id::text FROM public.vote_revision_issues i
                      WHERE i.record_id = v.record_id AND i.revision_number = v.revision_number AND i.is_tracked) AS issue_ids,
               CASE e.vote_cast WHEN 'Yea' THEN 'Yea' WHEN 'Aye' THEN 'Yea' WHEN 'Nay' THEN 'Nay' WHEN 'No' THEN 'Nay' END AS side,
               CASE e.party WHEN 'Democrat' THEN v.democrat_majority WHEN 'Republican' THEN v.republican_majority END AS party_majority_cast,
               e.party,
               v.alignment,
               v.columns_written AS alignment_known
        """;

    internal const string MemberVotesFrom = """
          FROM public_api.active_records a
          JOIN public.vote_revisions v ON v.record_id = a.record_id AND v.revision_number = a.revision_number
          JOIN public.vote_member_list_entries e ON e.list_id = v.member_list_id
         WHERE a.kind = 'vote'
        """;

    // breaks_party is null when the vote has no party line for the member: not a Split vote, a party other than Democrat or Republican, no Yea or Nay, or no party majority.
    // Otherwise it is true when the member cast the other side than the party majority.
    internal const string BreaksPartyColumn = """
               CASE WHEN v.alignment = 'Split' AND e.party IN ('Democrat', 'Republican')
                         AND e.vote_cast IN ('Yea', 'Aye', 'Nay', 'No')
                         AND CASE e.party WHEN 'Democrat' THEN v.democrat_majority WHEN 'Republican' THEN v.republican_majority END IS NOT NULL
                    THEN (CASE e.vote_cast WHEN 'Yea' THEN 'Yea' WHEN 'Aye' THEN 'Yea' ELSE 'Nay' END)
                         <> CASE e.party WHEN 'Democrat' THEN v.democrat_majority WHEN 'Republican' THEN v.republican_majority END
               END AS breaks_party
        """;

    /// <summary>Creates the view <c>official_party_lines</c> from <c>breaks_party</c>. A later migration that rebuilds <c>member_votes</c> rebuilds it too.</summary>
    internal const string PartyLinesSql = """
        CREATE OR REPLACE VIEW public_api.official_party_lines WITH (security_barrier = true) AS
        SELECT m.official_id, m.chamber, m.congress, m.party,
               count(*) FILTER (WHERE NOT m.breaks_party) AS with_party,
               count(*) FILTER (WHERE m.breaks_party) AS against_party,
               count(*) AS cast_count,
               min(m.voted_on) AS first_voted_on, max(m.voted_on) AS last_voted_on
          FROM public_api.member_votes m
         WHERE m.breaks_party IS NOT NULL
         GROUP BY m.official_id, m.chamber, m.congress, m.party;
        """;

    private static readonly string UpSql = $"""
        CREATE OR REPLACE VIEW public_api.member_votes WITH (security_barrier = true) AS
        {MemberVotesColumns},
        {BreaksPartyColumn}
        {MemberVotesFrom};

        {PartyLinesSql}
        """;

    private static readonly string DownSql = $"""
        DROP VIEW public_api.official_party_lines;
        DROP VIEW public_api.member_votes;

        CREATE VIEW public_api.member_votes WITH (security_barrier = true) AS
        {MemberVotesColumns}
        {MemberVotesFrom};

        CREATE VIEW public_api.official_party_lines WITH (security_barrier = true) AS
        SELECT m.official_id, m.chamber, m.congress, m.party,
               count(*) FILTER (WHERE m.side = m.party_majority_cast) AS with_party,
               count(*) FILTER (WHERE m.side <> m.party_majority_cast) AS against_party,
               count(*) AS cast_count,
               min(m.voted_on) AS first_voted_on, max(m.voted_on) AS last_voted_on
          FROM public_api.member_votes m
         WHERE m.alignment = 'Split'
           AND m.party IN ('Democrat', 'Republican')
           AND m.side IS NOT NULL AND m.party_majority_cast IS NOT NULL
         GROUP BY m.official_id, m.chamber, m.congress, m.party;

        ALTER VIEW public_api.member_votes OWNER TO civic_lens_public_owner;
        ALTER VIEW public_api.official_party_lines OWNER TO civic_lens_public_owner;
        GRANT SELECT ON public_api.member_votes, public_api.official_party_lines TO civic_lens_public;
        """;

    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(UpSql);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(DownSql);
}
