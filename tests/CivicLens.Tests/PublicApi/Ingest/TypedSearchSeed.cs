using System.Globalization;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.PublicApi.Votes;

namespace CivicLens.Tests.PublicApi.Ingest;

/// <summary>
/// Writes the entries and the typed rows that the <c>public_api</c> views read, for the sample records of <see cref="PublishedSamples"/>. The texts come from the record bodies.
/// Each record cites its own extraction rows, which hold the source texts of the record. A later revision of a record reuses the source rows of its first revision.
/// <see cref="TypedReleaseSite"/> lists the records in releases.
/// </summary>
internal static class TypedSearchSeed
{
    public static async Task WriteAsync(string database, SampleRecord[] records)
    {
        if (records.Length == 0) return;
        await PublishedEntryFixtures.SeedEntriesAsync(database, [.. records.Select(record => record.Entry)], allowMissingRevisionParents: true);
        foreach (var record in records) await ViewsSeed.ExecuteAsync(database, "SET session_replication_role = replica;\n" + Rows(record));
    }

    private static string Rows(SampleRecord record) => record.Body switch
    {
        PublishedStatement statement => Statement(statement),
        PublishedDocumentChange change => DocumentChange(change),
        PublishedAction action => Action(action),
        PublishedVote vote => Vote(vote),
        PublishedExecutiveAction executive => ExecutiveAction(executive),
        PublishedBrief brief => Brief(brief),
        PublishedRelationship relationship => Relationship(relationship),
        _ => throw new InvalidOperationException("The sample kind is unknown.")
    };

    private static string Statement(PublishedStatement record)
    {
        var extraction = ExtractionId(record.RecordId, "e");
        return $"""
            {Extraction(extraction, record.Source.Url, record.Source.Text)}
            INSERT INTO statement_candidates (candidate_id, source_id, url, extraction_id, attempt_id, created_at_utc_ticks)
              VALUES ('{record.RecordId}', 'src', {Q(record.Source.Url)}, '{extraction}', 'attempt-{extraction}', 1) ON CONFLICT DO NOTHING;
            INSERT INTO statement_revisions (candidate_id, revision_number, author_subject, author_kind, revision_json, created_at_utc_ticks, headline, summary, statement_date)
              VALUES ('{record.RecordId}', {record.RevisionNumber}, 'test', 'Person', 'x', 1, {Q(record.Headline)}, {Q(record.Summary)}, NULL);
            {string.Join('\n', record.Quotations.Select((quotation, position) => $"INSERT INTO statement_revision_quotations (candidate_id, revision_number, position, quotation_extraction_id, quotation_start, quotation_length, kind) VALUES ('{record.RecordId}', {record.RevisionNumber}, {position}, '{extraction}', {quotation.Start}, {quotation.Length}, 'DirectQuotation');"))}
            {string.Join('\n', record.Speakers.Select((speaker, position) => $"INSERT INTO statement_revision_speakers (candidate_id, revision_number, official_id, position) VALUES ('{record.RecordId}', {record.RevisionNumber}, {Q(speaker.Id)}, {position});"))}
            {string.Join('\n', record.IssueIds.Select((issue, position) => $"INSERT INTO statement_revision_issues (candidate_id, revision_number, issue_id, position) VALUES ('{record.RecordId}', {record.RevisionNumber}, {Q(issue)}, {position});"))}
            """;
    }

    private static string DocumentChange(PublishedDocumentChange record)
    {
        string before = ExtractionId(record.RecordId, "b"), after = ExtractionId(record.RecordId, "a"), comparison = ExtractionId(record.RecordId, "c");
        return $"""
            {Extraction(before, record.Before.Url, record.Before.Text)}
            {Extraction(after, record.After.Url, record.After.Text)}
            INSERT INTO document_comparisons (comparison_id, before_extraction_id, after_extraction_id, algorithm_version, settings_version, result_json, has_changes, hunk_count)
              VALUES ('{comparison}', '{before}', '{after}', 'a', 's', 'x', true, 1) ON CONFLICT DO NOTHING;
            INSERT INTO document_change_revisions (draft_id, revision_number, revision_json, created_at_utc_ticks, author_kind, author_subject, comparison_id, headline, summary, institution)
              VALUES ('{record.RecordId}', {record.RevisionNumber}, 'x', 1, 'Person', 'test', '{comparison}', {Q(record.Headline)}, {Q(record.Summary)}, {Q(record.Institution)});
            """;
    }

    private static string Action(PublishedAction record)
    {
        var extraction = ExtractionId(record.RecordId, "e");
        var sponsor = record.Sponsorships[0];
        var sponsoredOn = sponsor.SponsoredOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return $"""
            {Extraction(extraction, record.Sources[0].Url, record.Sources[0].Text)}
            {PublicActionRows.ActionRevision(record.RecordId, record.RevisionNumber, "Model", sponsoredOn, 0, record.Congress, record.BillType, record.BillNumber, policyAreaId: null, headline: "placeholder")}
            UPDATE action_revisions SET headline = {Q(record.Headline)}, summary = {Q(record.Summary)}, bill_title = {Q(record.BillTitle)}
             WHERE record_id = '{record.RecordId}' AND revision_number = {record.RevisionNumber};
            {PublicActionRows.ActionSponsorship(record.RecordId, record.RevisionNumber, 0, sponsor.Official.Id, "Sponsor", true, sponsoredOn)}
            INSERT INTO action_revision_sponsorship_citations (record_id, revision_number, sponsorship_position, position, citation_extraction_id, citation_start, citation_length)
              VALUES ('{record.RecordId}', {record.RevisionNumber}, 0, 0, '{extraction}', 0, 2);
            """;
    }

    private static string Vote(PublishedVote record)
    {
        var extraction = ExtractionId(record.RecordId, "e");
        var list = new string('3', 64);
        return $"""
            {Extraction(extraction, record.Sources[0].Url, record.Sources[0].Text)}
            INSERT INTO vote_member_lists (list_id, member_count, created_at_utc_ticks, yea_count, nay_count, present_count, not_voting_count, other_count)
              VALUES ('{list}', 1, 1, 1, 0, 0, 0, 0) ON CONFLICT DO NOTHING;
            INSERT INTO vote_revisions (record_id, revision_number, author_subject, author_kind, created_at_utc_ticks, {VoteRevisionColumnsSeed.RequiredColumns})
              VALUES ('{record.RecordId}', {record.RevisionNumber}, 'test', 'Person', 1, {Q(record.Headline)}, {Q(record.Summary)}, 'Model', 'src', 'CongressGov', 'House', 119, 1, 100,
                  'candidate', 1, DATE '2025-04-10', '{extraction}', 0, 1, 'Other', '{list}', '{new string('2', 64)}', true, 'Passed');
            """;
    }

    private static string ExecutiveAction(PublishedExecutiveAction record)
    {
        var extraction = ExtractionId(record.RecordId, "e");
        return $"""
            {Extraction(extraction, record.Sources[0].Url, record.Sources[0].Text)}
            INSERT INTO executive_action_revisions (record_id, revision_number, revision_json, created_at_utc_ticks, author_kind, author_subject, code_version, source_id,
                document_number, kind, title, signed_on, published_on, federal_register_citation, president_name, person_id, document_url)
              VALUES ('{record.RecordId}', {record.RevisionNumber}, 'x', 1, 'Code', 'civic-lens:code', '{new string('c', 40)}', 'fr', {Q(record.DocumentNumber)}, 'ExecutiveOrder',
                  {Q(record.Headline)}, '2025-01-20', '2025-01-28', '90 FR 8235', 'Jane Roe', 'o1', {Q(record.DocumentUrl)});
            INSERT INTO executive_action_revision_facts (record_id, revision_number, position, citation_extraction_id, citation_start, citation_length)
              VALUES ('{record.RecordId}', {record.RevisionNumber}, 0, '{extraction}', 0, 10);
            """;
    }

    private static string Brief(PublishedBrief record) => $"""
        INSERT INTO brief_revisions (record_id, revision_number, author_subject, author_kind, revision_json, created_at_utc_ticks, headline, text_origin, week_start,
            window_start_utc_ticks, window_end_utc_ticks, first_release_number, cutoff_release_number, fingerprint, coverage_considered, coverage_sent_to_model)
          VALUES ('{record.RecordId}', {record.RevisionNumber}, 'test', 'Person', 'x', 1, {Q(record.Headline)}, 'Person', '2026-10-05', 1, 6048000000001, 1, 1, REPEAT('a', 64), 2, 1);
        {string.Join('\n', record.Paragraphs.Select((paragraph, position) => $"INSERT INTO brief_revision_paragraphs (record_id, revision_number, part, position, paragraph_text) VALUES ('{record.RecordId}', {record.RevisionNumber}, 'Text', {position}, {Q(paragraph.Text)});"))}
        """;

    // The quote spans the source text of the statement that the relationship cites, which the statement rows hold.
    private static string Relationship(PublishedRelationship record)
    {
        var start = record.Quote.Length == 0 ? 0 : QuoteStart;
        return $"""
            {PublicRecordPageRows.RelationshipRevision(record.RecordId, record.RevisionNumber, record.Statement.RecordId, record.Target.RecordId, "action", "DescribesOwnBill", "placeholder")}
            UPDATE relationship_revisions SET reason = {Q(record.Reason)}, statement_quote_extraction_id = '{ExtractionId(record.Statement.RecordId, "e")}',
                statement_quote_start = {start}, statement_quote_length = {record.Quote.Length},
                match_extraction_id = '{ExtractionId(record.Statement.RecordId, "e")}', match_start = {start}, match_length = 1
             WHERE record_id = '{record.RecordId}' AND revision_number = {record.RevisionNumber};
            """;
    }

    /// <summary>The offset of the relationship quote in the statement source text of the fixture.</summary>
    public const int QuoteStart = 3;

    private static string ExtractionId(string recordId, string part) => (part + recordId).PadRight(64, '0')[..64];

    private static string Extraction(string id, string url, string text) => $"""
        INSERT INTO collection_attempts (attempt_id, source_id, requested_url, final_url, observed_at_utc_ticks, outcome, has_response, response_status_code,
            response_content_encodings, has_sent_validators, capture_sha256, elapsed_milliseconds)
          VALUES ('attempt-{id}', 'src', {Q(url)}, {Q(url)}, 1, 'captured', true, 200, ARRAY[]::text[], false, REPEAT('a', 64), 0) ON CONFLICT DO NOTHING;
        INSERT INTO document_extractions (extraction_id, attempt_id, extraction_version, text, text_sha256)
          VALUES ('{id}', 'attempt-{id}', 'test', {Q(text)}, REPEAT('b', 64)) ON CONFLICT DO NOTHING;
        """;

    public static string Q(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
}
