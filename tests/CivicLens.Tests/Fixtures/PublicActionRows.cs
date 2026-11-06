namespace CivicLens.Tests.Fixtures;

/// <summary>SQL that writes the action rows that the <c>public_api</c> bill views read.</summary>
internal static class PublicActionRows
{
    // Run ID and code version satisfy the author checks. The Code row also carries the bill citation that the Code author needs.
    public static string ActionRevision(string record, int revision, string origin, string? eventOn, int? cosponsorCount,
        int congress = 119, string billType = "hr", int billNumber = 7, string? policyAreaId = "health", string? headline = null)
    {
        var authorKind = origin switch { "Model" => "Analysis", "Code" => "Code", _ => "Person" };
        var run = authorKind == "Person" ? "NULL" : $"'run-{record[^Math.Min(record.Length, 20)..]}'";
        var codeVersion = authorKind == "Code" ? $"'{new string('c', 40)}'" : "NULL";
        var billCitation = authorKind == "Code" ? "'ext', 0, 1" : "NULL, NULL, NULL";
        var policyArea = policyAreaId is null
            ? "NULL, NULL, NULL, NULL, NULL"
            : $"'{char.ToUpperInvariant(policyAreaId[0])}{policyAreaId[1..]}', 'ext', 0, 1, '{policyAreaId}'";
        return $"""
            INSERT INTO action_revisions (record_id, revision_number, author_subject, author_kind, run_id, code_version, revision_json, created_at_utc_ticks,
                headline, summary, text_origin, source_id, congress, bill_type, bill_number, candidate_id, candidate_update_utc_ticks,
                candidate_includes_summaries, candidate_includes_subjects, fingerprint, bill_title, issue_revision_id, issue_candidates_recorded,
                bill_citation_extraction_id, bill_citation_start, bill_citation_length,
                policy_area, policy_area_citation_extraction_id, policy_area_citation_start, policy_area_citation_length, policy_area_id,
                suggestion_headline, suggestion_summary, suggestion_run_id, suggestion_fingerprint, event_on, cosponsor_count)
            VALUES ('{record}', {revision}, 'test', '{authorKind}', {run}, {codeVersion}, 'x', 1,
                '{headline ?? $"Headline {record}"}', 'Summary {record}', '{origin}', 'src', {congress}, '{billType}', {billNumber}, '{new string('b', 64)}', 1,
                true, true, '{new string('d', 64)}', 'A housing bill', '{new string('e', 64)}', true,
                {billCitation}, {policyArea},
                'Model draft headline', 'Model draft summary', 'suggestion-run', '{new string('f', 64)}',
                {(eventOn is null ? "NULL" : $"'{eventOn}'")}, {cosponsorCount?.ToString() ?? "NULL"});
            """;
    }

    public static string ActionSponsorship(string record, int revision, int position, string person, string role, bool original, string sponsoredOn, string? withdrawnOn = null) => $"""
        INSERT INTO action_revision_sponsorships (record_id, revision_number, "position", person_id, bioguide_id, role, is_original, sponsored_on, withdrawn_on)
        VALUES ('{record}', {revision}, {position}, '{person}', 'B{person}', '{role}', {original}, '{sponsoredOn}', {(withdrawnOn is null ? "NULL" : $"'{withdrawnOn}'")});
        """;

    public static string ActionIssue(string record, int revision, string issue, int position, bool tracked) => $"""
        INSERT INTO action_revision_issues (record_id, revision_number, issue_id, "position", is_tracked) VALUES ('{record}', {revision}, '{issue}', {position}, {tracked});
        """;
}
