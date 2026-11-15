using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations;

/// <summary>
/// Gives a Senate vote with code text the label "Written automatically from Senate roll call data, with no AI". A House vote, a bill and an action keep the
/// Congress.gov label. The three views that carry a vote label repeat <c>AiLabelWords.CodeWording</c> and <c>AiLabelWords.SenateCodeWording</c>, because SQL
/// cannot read the class. Tests compare them. The views are <c>votes</c>, <c>member_votes</c> and <c>record_headlines</c>. The rollback gives every vote
/// the Congress.gov label again.
/// </summary>
public partial class ChamberVoteCodeLabel : Migration
{
    private const string ModelLabel = "An AI summary of the official record";

    private const string HouseCodeLabel = "Written automatically from Congress.gov data, with no AI";

    private const string SenateCodeLabel = "Written automatically from Senate roll call data, with no AI";

    private const string StatementCodeLabel = "An AI summary of this press release.";

    private const string StatementPersonLabel = "Drafted by an AI model from this source. A person checked it before publication.";

    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(VoteLabelViews(byChamber: true));

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(VoteLabelViews(byChamber: false));

    private static string VoteCodeLabel(string revision, bool byChamber) => byChamber
        ? $"CASE {revision}.chamber WHEN 'Senate' THEN '{SenateCodeLabel}' ELSE '{HouseCodeLabel}' END"
        : $"'{HouseCodeLabel}'";

    private static string VoteLabelViews(bool byChamber) => $$"""
        CREATE OR REPLACE VIEW public_api.votes WITH (security_barrier = true) AS
        SELECT a.record_id, a.revision_number, a.release_number,
               v.chamber, v.congress, v.session_number, v.roll_call_number, v.voted_on, v.kind, v.bill_type, v.bill_number,
               v.headline, v.summary, v.text_origin,
               CASE v.text_origin
                 WHEN 'Model' THEN '{{ModelLabel}}'
                 WHEN 'Code' THEN {{VoteCodeLabel("v", byChamber)}}
               END AS label,
               CASE WHEN t.other_count = 0 THEN t.yea_count::bigint END AS yea,
               CASE WHEN t.other_count = 0 THEN t.nay_count::bigint END AS nay,
               CASE WHEN t.other_count = 0 THEN t.present_count::bigint END AS present,
               CASE WHEN t.other_count = 0 THEN t.not_voting_count::bigint END AS not_voting,
               v.question, v.result,
               v.alignment,
               bill.record_id::varchar(64) AS bill_record_id
          FROM public_api.active_records a
          JOIN public.vote_revisions v ON v.record_id = a.record_id AND v.revision_number = a.revision_number
          LEFT JOIN public.release_vote_bills rb ON rb.release_number = a.release_number AND rb.vote_record_id = a.record_id
          LEFT JOIN LATERAL (SELECT b.record_id FROM public_api.bills b WHERE b.record_id = rb.bill_record_id LIMIT 1) bill ON TRUE
          JOIN public.vote_member_lists t ON t.list_id = v.member_list_id
         WHERE a.kind = 'vote' AND v.member_list_id IS NOT NULL;

        CREATE OR REPLACE VIEW public_api.member_votes WITH (security_barrier = true) AS
        SELECT e.person_id AS official_id, a.record_id,
               v.chamber, v.congress, v.session_number, v.roll_call_number, v.voted_on, v.kind, v.bill_type, v.bill_number,
               v.headline,
               CASE v.text_origin
                 WHEN 'Model' THEN '{{ModelLabel}}'
                 WHEN 'Code' THEN {{VoteCodeLabel("v", byChamber)}}
               END AS label,
               v.result, e.vote_cast,
               ARRAY(SELECT i.issue_id::text FROM public.vote_revision_issues i
                      WHERE i.record_id = v.record_id AND i.revision_number = v.revision_number AND i.is_tracked) AS issue_ids,
               CASE e.vote_cast WHEN 'Yea' THEN 'Yea' WHEN 'Aye' THEN 'Yea' WHEN 'Nay' THEN 'Nay' WHEN 'No' THEN 'Nay' END AS side,
               CASE e.party WHEN 'Democrat' THEN v.democrat_majority WHEN 'Republican' THEN v.republican_majority END AS party_majority_cast,
               e.party,
               v.alignment,
               v.columns_written AS alignment_known,
               CASE WHEN v.alignment = 'Split' AND e.party IN ('Democrat', 'Republican')
                         AND e.vote_cast IN ('Yea', 'Aye', 'Nay', 'No')
                         AND CASE e.party WHEN 'Democrat' THEN v.democrat_majority WHEN 'Republican' THEN v.republican_majority END IS NOT NULL
                    THEN (CASE e.vote_cast WHEN 'Yea' THEN 'Yea' WHEN 'Aye' THEN 'Yea' ELSE 'Nay' END)
                         <> CASE e.party WHEN 'Democrat' THEN v.democrat_majority WHEN 'Republican' THEN v.republican_majority END
               END AS breaks_party,
               v.text_origin
          FROM public_api.active_records a
          JOIN public.vote_revisions v ON v.record_id = a.record_id AND v.revision_number = a.revision_number
          JOIN public.vote_member_list_entries e ON e.list_id = v.member_list_id
         WHERE a.kind = 'vote';

        CREATE OR REPLACE VIEW public_api.record_headlines WITH (security_barrier = true) AS
        SELECT a.kind, a.record_id, r.headline, r.text_origin, CASE r.text_origin
                 WHEN 'Model' THEN '{{ModelLabel}}'
                 WHEN 'Code' THEN '{{HouseCodeLabel}}'
               END AS label
          FROM public_api.active_records a
          JOIN public.action_revisions r ON r.record_id = a.record_id AND r.revision_number = a.revision_number
         WHERE a.kind = 'action' AND r.headline IS NOT NULL
        UNION ALL
        SELECT a.kind, a.record_id, r.headline, r.text_origin, CASE r.text_origin
                 WHEN 'Model' THEN '{{ModelLabel}}'
                 WHEN 'Code' THEN {{VoteCodeLabel("r", byChamber)}}
               END
          FROM public_api.active_records a
          JOIN public.vote_revisions r ON r.record_id = a.record_id AND r.revision_number = a.revision_number
         WHERE a.kind = 'vote' AND r.headline IS NOT NULL
        UNION ALL
        SELECT a.kind, a.record_id, r.headline, CASE r.author_kind WHEN 'Analysis' THEN 'Model' ELSE 'Person' END,
               CASE WHEN r.author_kind = 'Analysis' THEN
                    CASE e.approved_by WHEN 'Code' THEN '{{StatementCodeLabel}}' WHEN 'Person' THEN '{{StatementPersonLabel}}' END
               END
          FROM public_api.active_records a
          JOIN public.statement_revisions r ON r.candidate_id = a.record_id AND r.revision_number = a.revision_number
          JOIN public.published_entries e ON e.kind = a.kind AND e.record_id = a.record_id AND e.revision_number = a.revision_number
         WHERE a.kind = 'statement' AND r.headline IS NOT NULL AND e.approved_by IS NOT NULL;
        """;
}
