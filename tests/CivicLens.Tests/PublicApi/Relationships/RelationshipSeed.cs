using CivicLens.Tests.Fixtures;
using CivicLens.Tests.PublicApi.Votes;

namespace CivicLens.Tests.PublicApi.Relationships;

/// <summary>Seeds one statement, one bill, and one vote of the active release 1, and relationships that link the statement to either target. The quote cites
/// <see cref="Source"/>, which holds characters outside the Basic Multilingual Plane before the quote, so a cut that counts code points picks the wrong text.</summary>
internal static class RelationshipSeed
{
    public static readonly string StatementId = new('5', 64);
    public static readonly string ActionId = new('a', 64);
    public static readonly string VoteId = new('1', 64);
    public static readonly string BillLink = new('7', 64);
    public static readonly string VoteLink = new('8', 64);
    public static readonly string ExtractionId = new('c', 64);

    public const string Source = "\U0001F600 Intro, café \U0001F3DB️. Doe said “Rent must fall now” at the hall. \U0001F600 End.";
    public const string Quote = "“Rent must fall now”";
    public const string Reason = "Doe’s statement names the bill’s rent cap.";
    public const string ActionHeadline = "Housing bill";
    public const string ActionSummary = "The bill caps rent ≤ 3%.";

    public static readonly string[] VoteFactTexts = ["Question: On passage", "Result: Passed 218–210"];

    public static int QuoteStart => Source.IndexOf(Quote, StringComparison.Ordinal);

    public static async Task SeedAsync(string database)
    {
        await ViewsSeed.ExecuteAsync(database, $"""
            SET session_replication_role = replica;
            {PublicRecordPageRows.Headline("statement", StatementId, "Doe on rent", "Model", "Person")}
            {PublicRecordPageRows.Headline("action", ActionId, ActionHeadline, "Model")}
            {PublicRecordPageRows.Headline("vote", VoteId, "Vote on housing", "Code")}
            UPDATE action_revisions SET summary = '{ActionSummary}' WHERE record_id = '{ActionId}';
            INSERT INTO vote_revision_facts (record_id, revision_number, position, fact_text, citation_extraction_id, citation_start, citation_length) VALUES
              ('{VoteId}', 1, 1, '{VoteFactTexts[1]}', 'ext', 0, 1), ('{VoteId}', 1, 0, '{VoteFactTexts[0]}', 'ext', 0, 1);
            INSERT INTO collection_attempts (attempt_id, source_id, requested_url, final_url, observed_at_utc_ticks, outcome, has_response, response_status_code,
                response_content_encodings, has_sent_validators, capture_sha256, elapsed_milliseconds)
              VALUES ('attempt-quote', 'src', 'https://doe.example/release', 'https://doe.example/release', 1, 'captured', true, 200, ARRAY[]::text[], false, REPEAT('a', 64), 0);
            INSERT INTO document_extractions (extraction_id, attempt_id, extraction_version, text, text_sha256)
              VALUES ('{ExtractionId}', 'attempt-quote', 'test', '{Source}', REPEAT('b', 64));
            {Relationship(BillLink, ActionId, "action", "DescribesOwnBill", "2026-05-01", "F4", model: true)}
            {Relationship(VoteLink, VoteId, "vote", "StatementAboutVote", null, "F2", model: false)}
            INSERT INTO published_entry_officials (kind, record_id, revision_number, position, official_id, is_matched)
              VALUES ('relationship', '{BillLink}', 1, 0, 'official-z', true), ('relationship', '{BillLink}', 1, 1, 'official-a', false);
            INSERT INTO published_entry_issues (kind, record_id, revision_number, position, issue_id) VALUES ('relationship', '{BillLink}', 1, 0, 'housing');
            UPDATE published_entries SET headline = 'Doe on rent and {ActionHeadline}', approved_by = 'Person', approved_at_utc_ticks = 639133272000000000
             WHERE kind = 'relationship' AND record_id = '{BillLink}';
            INSERT INTO published_records (kind, record_id, first_published_at_utc_ticks) VALUES ('relationship', '{BillLink}', 639133275000000000);
            """);
    }

    /// <summary>A relationship of revision 1 whose quote cites <see cref="Source"/>. A model reason has the author kind Analysis and its own run.</summary>
    private static string Relationship(string link, string target, string targetKind, string type, string? statementDate, string factId, bool model) => $"""
        {PublicRecordPageRows.Link(link, StatementId, target, targetKind, type, statementDate)}
        {PublicRecordPageRows.RelationshipRevision(link, 1, StatementId, target, targetKind, type, Reason)}
        UPDATE relationship_revisions SET author_kind = '{(model ? "Analysis" : "Person")}', run_id = {(model ? $"'run-{link[..8]}'" : "NULL")},
            statement_quote_extraction_id = '{ExtractionId}', statement_quote_start = {QuoteStart}, statement_quote_length = {Quote.Length},
            match_extraction_id = '{ExtractionId}', match_start = {QuoteStart + 1}, match_length = 4, target_fact_id = '{factId}'
         WHERE record_id = '{link}';
        """;
}
