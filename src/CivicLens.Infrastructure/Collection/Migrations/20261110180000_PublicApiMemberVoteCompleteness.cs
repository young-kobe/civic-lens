using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations;

public partial class PublicApiMemberVoteCompleteness : Migration
{
    private const string UpSql = """
        CREATE OR REPLACE VIEW public_api.member_votes WITH (security_barrier = true) AS
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
          FROM public_api.active_records a
          JOIN public.vote_revisions v ON v.record_id = a.record_id AND v.revision_number = a.revision_number
          JOIN public.vote_member_list_entries e ON e.list_id = v.member_list_id
         WHERE a.kind = 'vote';
        """;

    private const string DownSql = """
        DROP VIEW public_api.official_party_lines;
        DROP VIEW public_api.member_votes;

        CREATE VIEW public_api.member_votes WITH (security_barrier = true) AS
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
               v.alignment
          FROM public_api.active_records a
          JOIN public.vote_revisions v ON v.record_id = a.record_id AND v.revision_number = a.revision_number
          JOIN public.vote_member_list_entries e ON e.list_id = v.member_list_id
         WHERE a.kind = 'vote';

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
        REVOKE SELECT (columns_written) ON public.vote_revisions FROM civic_lens_public_owner;
        """;

    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(UpSql);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(DownSql);
}
