namespace CivicLens.Tests.PublicApi;

/// <summary>Copies complete House and Senate member lists for the public read scale tests.</summary>
internal static class ProductionSizeVotes
{
    // Votes spread over 700 days, so several votes share a day. Three of every four have a substantive kind. A third split the parties.
    // Members of the first half are Democrats. A member votes No on one vote in three and does not vote on one in fifty.
    public static string CopiesSql(char template, string chamber, int votes, int members) => $$"""
        CREATE TEMP TABLE copies_{{template}} AS
        SELECT n, CASE WHEN n = 0 THEN repeat('{{template}}', 64) ELSE md5('{{chamber}}' || n) || md5('x' || n) END AS id,
               (ARRAY['Passage', 'Suspension', 'Amendment', 'Nomination', 'Rule', 'Passage', 'ConferenceReport', 'Amendment', 'Cloture', 'VetoOverride', 'Passage', 'MotionToRecommit'])[1 + n % 12] AS kind
          FROM generate_series(0, {{votes - 1}}) n;
        INSERT INTO publication_record_states (kind, record_id, first_release, revision_number, last_release)
        SELECT 'vote', id, 1, 1, NULL FROM copies_{{template}} WHERE n > 0;
        INSERT INTO vote_member_lists
        SELECT (jsonb_populate_record(NULL::vote_member_lists, to_jsonb(l) || jsonb_build_object('list_id', c.id))).*
          FROM vote_member_lists l, copies_{{template}} c WHERE l.list_id = repeat('{{template}}', 64) AND c.n > 0;
        INSERT INTO vote_revisions
        SELECT (jsonb_populate_record(NULL::vote_revisions, to_jsonb(v) || jsonb_build_object('record_id', c.id, 'member_list_id', c.id, 'roll_call_number', c.n + 1)
                 || jsonb_build_object('candidate_id', 'cand' || c.n))).*
          FROM vote_revisions v, copies_{{template}} c WHERE v.record_id = repeat('{{template}}', 64) AND c.n > 0;
        UPDATE vote_revisions v SET voted_on = DATE '2025-01-03' + c.n * 700 / {{votes}}, kind = c.kind, bill_type = 'hr', bill_number = 1 + c.n % 400,
               columns_written = true, alignment = CASE WHEN c.n % 3 = 0 THEN 'Split' ELSE 'PartiesAgree' END,
               democrat_majority = 'Yea', republican_majority = CASE WHEN c.n % 3 = 0 THEN 'Nay' ELSE 'Yea' END
          FROM copies_{{template}} c WHERE v.record_id = c.id;
        INSERT INTO vote_member_list_entries (list_id, "position", person_id, bioguide_id, vote_cast, citation_extraction_id, citation_start, citation_length, party)
        SELECT c.id, e."position", e.person_id, e.bioguide_id,
               CASE WHEN (c.n + e."position") % 50 = 0 THEN 'Not Voting' WHEN (c.n + e."position") % 3 = 0 THEN 'Nay' ELSE 'Yea' END,
               e.citation_extraction_id, e.citation_start, e.citation_length, CASE WHEN e."position" < {{members / 2}} THEN 'Democrat' ELSE 'Republican' END
          FROM vote_member_list_entries e, copies_{{template}} c WHERE e.list_id = repeat('{{template}}', 64) AND c.n > 0;
        UPDATE vote_member_list_entries SET party = CASE WHEN "position" < {{members / 2}} THEN 'Democrat' ELSE 'Republican' END WHERE list_id = repeat('{{template}}', 64);
        INSERT INTO vote_revision_issues (record_id, revision_number, issue_id, position, is_tracked)
        SELECT id, 1, 'health', 0, true FROM copies_{{template}} WHERE n % 4 = 1;
        """;
}
