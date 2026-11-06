using System.Globalization;

namespace CivicLens.Tests.Fixtures;

/// <summary>SQL that writes the rows that the record page views of <c>public_api</c> read: bill gates, relationship entries, and executive actions. Run it with <c>session_replication_role = replica</c>.</summary>
internal static class PublicRecordPageRows
{
    public const string ActiveRelease = """
        INSERT INTO publication_releases (release_number, directory_name, actor_subject, published_at_utc_ticks, record_count, is_active)
          VALUES (1, '000001-00000000000000000000000000000001', 'test', 1, 1, true) ON CONFLICT DO NOTHING;
        """;

    // Each gate row is one version of the bill. The detail job ID is unique for the row.
    public static string Gate(int billNumber, int version, long updateTicks, string? text, string? date, long createdTicks) => $"""
        INSERT INTO bill_gates (detail_job_id, source_id, congress, bill_type, bill_number, update_date_utc_ticks, cosponsor_count, summary_count, issue_revision_id,
            has_tracked_issue, decision, reason, created_at_utc_ticks, decided_at_utc_ticks, latest_action_read, latest_action_text, latest_action_date)
          VALUES ('job-{billNumber}-{version}', 'src', 119, 'hr', {billNumber}, {updateTicks}, 0, 0, '{new string('a', 64)}', true, 'Passed', 'CoveredSponsor',
                  {createdTicks}, {createdTicks}, true, {(text is null ? "NULL" : $"'{text}'")}, {(date is null ? "NULL" : $"'{date}'")});
        """;

    /// <summary>The draft row that <c>ReadBillStatusesAsync</c> joins to the gate rows of a bill.</summary>
    public static string ActionDraft(string record, int billNumber) => $"""
        INSERT INTO action_drafts (record_id, source_id, congress, bill_type, bill_number, current_revision_number, created_at_utc_ticks, last_revised_at_utc_ticks, review_state_version, review_status)
          VALUES ('{record}', 'src', 119, 'hr', {billNumber}, 1, 0, 0, 0, 'AwaitingReview');
        """;

    /// <summary>One relationship with its entry rows and the entry rows of both ends. A first release of 2 keeps a record out of the active release 1.</summary>
    public static string Link(string link, string statement, string statementHeadline, string target, string targetKind, string targetHeadline, string type,
        string? statementDate, int linkFirst = 1, int targetFirst = 1) => $"""
        {ActiveRelease}
        INSERT INTO publication_record_states (kind, record_id, first_release, revision_number, last_release) VALUES
          ('relationship', '{link}', {linkFirst}, 1, NULL),
          ('statement', '{statement}', 1, 1, NULL),
          ('{targetKind}', '{target}', {targetFirst}, 1, NULL)
          ON CONFLICT DO NOTHING;
        INSERT INTO published_entries (kind, record_id, revision_number, relationship_record_id, relationship_revision_number, headline, event_date, link_type,
            statement_record_id, target_kind, target_record_id)
          VALUES ('relationship', '{link}', 1, '{link}', 1, 'Link', {(statementDate is null ? "NULL" : $"'{statementDate}'")}, '{type}', '{statement}', '{targetKind}', '{target}');
        {Entry("statement", statement, statementHeadline)}
        {Entry(targetKind, target, targetHeadline)}
        """;

    // The check ck_published_entries_revision_key wants the typed key columns of the kind.
    public static string Entry(string kind, string record, string headline)
    {
        var columns = kind switch
        {
            "statement" => "statement_candidate_id, statement_revision_number",
            "action" => "action_record_id, action_revision_number",
            _ => "vote_record_id, vote_revision_number"
        };
        var billTitle = kind == "action" ? ", bill_title" : "";
        var billTitleValue = kind == "action" ? ", 'T'" : "";
        return $"""
            INSERT INTO published_entries (kind, record_id, revision_number, {columns}, headline{billTitle})
              VALUES ('{kind}', '{record}', 1, '{record}', 1, '{headline}'{billTitleValue}) ON CONFLICT DO NOTHING;
            """;
    }

    /// <summary>An executive action with two revisions and two correction notices. The active revision is 2 and it holds both notices in reverse position order.</summary>
    public static string ExecutiveAction(string record) => $"""
        {ActiveRelease}
        INSERT INTO publication_record_states (kind, record_id, first_release, revision_number, last_release) VALUES ('executiveAction', '{record}', 1, 2, NULL);
        INSERT INTO executive_action_revisions (record_id, revision_number, revision_json, created_at_utc_ticks, author_kind, author_subject, code_version,
            source_id, document_number, kind, title, signed_on, published_on, federal_register_citation, president_name, person_id, document_url)
          VALUES ('{record}', 1, 'x', 1, 'Code', 'civic-lens:code', '{new string('c', 40)}', 'fr', '2026-00001', 'ExecutiveOrder', 'Old title', '2026-01-20', '2026-01-23', '91 FR 1', 'Jane Roe', 'president', 'https://fr.example/1'),
                 ('{record}', 2, 'x', 1, 'Code', 'civic-lens:code', '{new string('c', 40)}', 'fr', '2026-00001', 'ExecutiveOrder', 'Title two', '2026-01-20', '2026-01-23', '91 FR 1', 'Jane Roe', 'president', 'https://fr.example/1');
        INSERT INTO executive_action_documents (source_id, document_number, record_id, revision_number, published_on, document_job_id, notice_url,
            notice_citation_extraction_id, notice_citation_start, notice_citation_length) VALUES
          ('fr', '2026-00009', '{record}', 2, '2026-02-01', 'job1', 'https://fr.example/9', 'ext', 0, 1),
          ('fr', '2026-00005', '{record}', 2, '2026-02-03', 'job2', 'https://fr.example/5', 'ext', 0, 1);
        INSERT INTO executive_action_revision_notices (record_id, revision_number, "position", source_id, document_number) VALUES
          ('{record}', 2, 1, 'fr', '2026-00005'), ('{record}', 2, 0, 'fr', '2026-00009'), ('{record}', 1, 0, 'fr', '2026-00009');
        """;
}
