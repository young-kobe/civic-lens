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

    /// <summary>The status that a release pinned for an action record. Release 2 is a release that is not active. Run it with <c>session_replication_role = replica</c>.</summary>
    public static string PinnedStatus(int release, string record, string text, string date, long readTicks, string? stage = null) => $"""
        {ActiveRelease}
        INSERT INTO publication_releases (release_number, directory_name, actor_subject, published_at_utc_ticks, record_count, is_active)
          VALUES (2, '000002-00000000000000000000000000000002', 'test', 2, 1, false) ON CONFLICT DO NOTHING;
        INSERT INTO release_bill_statuses (release_number, record_id, latest_action_text, latest_action_date, read_at_utc_ticks, status)
          VALUES ({release}, '{record}', '{text}', '{date}', {readTicks}, {(stage is null ? "NULL" : $"'{stage}'")});
        """;

    /// <summary>One approval of a statement revision by an actor. The latest review state version names the approver.</summary>
    public static string StatementApproval(string record, int revision, int version, string actor) => $"""
        INSERT INTO statement_review_decisions (decision_id, candidate_id, revision_number, review_state_version, kind, created_at_utc_ticks, actor_subject)
          VALUES ('{record[..30]}{version:00}', '{record}', {revision}, {version}, 'Approve', {version}, '{actor}');
        """;

    /// <summary>One relationship with its entry row and the record states of the relationship and of both ends. A first release of 2 keeps a record out of the active release 1.
    /// The headline of an end comes from <see cref="Headline"/>.</summary>
    public static string Link(string link, string statement, string target, string targetKind, string type,
        string? statementDate, int linkFirst = 1, int statementFirst = 1, int targetFirst = 1) => $"""
        {ActiveRelease}
        INSERT INTO publication_record_states (kind, record_id, first_release, revision_number, last_release) VALUES
          ('relationship', '{link}', {linkFirst}, 1, NULL),
          ('statement', '{statement}', {statementFirst}, 1, NULL),
          ('{targetKind}', '{target}', {targetFirst}, 1, NULL)
          ON CONFLICT DO NOTHING;
        INSERT INTO published_entries (kind, record_id, revision_number, relationship_record_id, relationship_revision_number, headline, event_date, link_type,
            statement_record_id, target_kind, target_record_id)
          VALUES ('relationship', '{link}', 1, '{link}', 1, 'Link', {(statementDate is null ? "NULL" : $"'{statementDate}'")}, '{type}', '{statement}', '{targetKind}', '{target}');
        """;

    /// <summary>A typed relationship revision for view tests. Run with <c>session_replication_role = replica</c>.</summary>
    public static string RelationshipRevision(string link, int revision, string statement, string target, string targetKind, string type, string reason) => $"""
        INSERT INTO relationship_revisions (record_id, revision_number, author_subject, author_kind, run_id, revision_json, created_at_utc_ticks,
            statement_candidate_id, statement_revision_number, target_kind, match_basis, match_extraction_id, match_start, match_length, type, reason,
            statement_quote_extraction_id, statement_quote_start, statement_quote_length, target_fact_id, target_action_record_id, target_action_revision_number,
            target_vote_record_id, target_vote_revision_number, official_id)
          VALUES ('{link}', {revision}, 'test', 'Person', NULL, 'x', 1, '{statement}', 1,
            '{(targetKind == "action" ? "Action" : "Vote")}', 'BillNumber', 'ext', 0, 1, '{type}', '{reason}', 'ext', 0, 1, 'fact',
            {(targetKind == "action" ? $"'{target}', 1" : "NULL, NULL")}, {(targetKind == "vote" ? $"'{target}', 1" : "NULL, NULL")}, 'official');
        """;

    /// <summary>The revision 1 of a statement, action, or vote, with its headline and text origin ("Model", "Code", or "Person"). Write it once for each record.
    /// A statement has no origin column, so "Model" gives the author kind Analysis and any other origin gives Person.</summary>
    public static string Headline(string kind, string record, string headline, string origin = "Person", string? approvedBy = null) => kind switch
    {
        "statement" => $"""
            INSERT INTO published_entries (kind, record_id, revision_number, statement_candidate_id, statement_revision_number, headline, approved_by)
              VALUES ('statement', '{record}', 1, '{record}', 1, '{headline}', '{approvedBy ?? (origin == "Model" ? "Code" : "Person")}');
            INSERT INTO statement_revisions (candidate_id, revision_number, author_kind, author_subject, run_id, created_at_utc_ticks, headline, summary, revision_json)
              VALUES ('{record}', 1, '{(origin == "Model" ? "Analysis" : "Person")}', 'test', {(origin == "Model" ? $"'run-{record[..16]}'" : "NULL")}, 1, '{headline}', 'Summary', 'x');
            """,
        "action" => PublicActionRows.ActionRevision(record, 1, origin, null, null, headline: headline),
        _ => $"""
            INSERT INTO vote_revisions (record_id, revision_number, author_subject, author_kind, run_id, created_at_utc_ticks, {VoteRevisionColumnsSeed.RequiredColumns})
              VALUES ('{record}', 1, 'test', '{(origin == "Person" ? "Person" : "Analysis")}', {(origin == "Person" ? "NULL" : $"'run-{record[..16]}'")}, 1, '{headline}', 'Summary', '{origin}', 'src', 'CongressGov', 'House', 119, 1, 1, 'c', 1,
                  '2026-02-01', 'ext', 0, 1, 'Passage', '{record}', '{new string('a', 64)}', true, 'Passed');
            """
    };

    /// <summary>The fact lines of the saved Federal Register record of <see cref="ExecutiveAction"/>. The disposition notes line has no citation until a test sets one.</summary>
    public const string ExecutiveActionFactsText = "Federal Register document: 2026-00001\nSigned: 2026-01-20\nDisposition notes: Amends EO 1\n" +
        "Correction notice: 2026-00009\nCorrection notice: 2026-00005";

    /// <summary>The start of a line in <see cref="ExecutiveActionFactsText"/>.</summary>
    public static int FactAt(string line) => ExecutiveActionFactsText.IndexOf(line, StringComparison.Ordinal);

    /// <summary>An executive action with two revisions and two correction notices. The active revision is 2 and it holds both notices in reverse position order.
    /// Its saved Federal Register record has the record ID as its extraction ID. Revision 2 cites two fact lines, written out of position order, and each notice
    /// cites its line. Revision 1 cites a line of its own.</summary>
    public static string ExecutiveAction(string record) => $"""
        {ActiveRelease}
        INSERT INTO publication_record_states (kind, record_id, first_release, revision_number, last_release) VALUES ('executiveAction', '{record}', 1, 2, NULL);
        INSERT INTO executive_action_revisions (record_id, revision_number, revision_json, created_at_utc_ticks, author_kind, author_subject, code_version,
            source_id, document_number, kind, title, signed_on, published_on, federal_register_citation, president_name, person_id, document_url)
          VALUES ('{record}', 1, 'x', 1, 'Code', 'civic-lens:code', '{new string('c', 40)}', 'fr', '2026-00001', 'ExecutiveOrder', 'Old title', '2026-01-20', '2026-01-23', '91 FR 1', 'Jane Roe', 'president', 'https://fr.example/1'),
                 ('{record}', 2, 'x', 1, 'Code', 'civic-lens:code', '{new string('c', 40)}', 'fr', '2026-00001', 'ExecutiveOrder', 'Title two', '2026-01-20', '2026-01-23', '91 FR 1', 'Jane Roe', 'president', 'https://fr.example/1');
        {ExecutiveActionSource(record, ExecutiveActionFactsText)}
        INSERT INTO executive_action_revision_facts (record_id, revision_number, "position", citation_extraction_id, citation_start, citation_length)
        VALUES ('{record}', 2, 1, '{record}', {FactAt("Signed")}, 18), ('{record}', 2, 0, '{record}', 0, 37), ('{record}', 1, 0, '{record}', 0, 9);
        INSERT INTO executive_action_documents (source_id, document_number, record_id, revision_number, published_on, document_job_id, notice_url,
            notice_citation_extraction_id, notice_citation_start, notice_citation_length) VALUES
          ('fr', '2026-00009', '{record}', 2, '2026-02-01', 'job1', 'https://fr.example/9', '{record}', {FactAt("Correction notice: 2026-00009")}, 29),
          ('fr', '2026-00005', '{record}', 2, '2026-02-03', 'job2', 'https://fr.example/5', '{record}', {FactAt("Correction notice: 2026-00005")}, 29);
        INSERT INTO executive_action_revision_notices (record_id, revision_number, "position", source_id, document_number) VALUES
          ('{record}', 2, 1, 'fr', '2026-00005'), ('{record}', 2, 0, 'fr', '2026-00009'), ('{record}', 1, 0, 'fr', '2026-00009');
        """;

    /// <summary>A saved Federal Register record: one attempt and one extraction whose ID is the record ID, so each record has its own source.</summary>
    public static string ExecutiveActionSource(string record, string text) => $"""
        INSERT INTO collection_attempts (attempt_id, source_id, requested_url, final_url, observed_at_utc_ticks,
            outcome, has_response, response_status_code, response_content_encodings, has_sent_validators, capture_sha256, elapsed_milliseconds)
        VALUES ('fr-{record}', 'fr', 'https://fr.example/api/{record[..8]}', 'https://fr.example/api/{record[..8]}', 1000, 'captured', true, 200, ARRAY[]::text[], false, REPEAT('a', 64), 0);
        INSERT INTO document_extractions (extraction_id, attempt_id, extraction_version, text, text_sha256)
        VALUES ('{record}', 'fr-{record}', 'test', '{text}', REPEAT('b', 64));
        """;
}
