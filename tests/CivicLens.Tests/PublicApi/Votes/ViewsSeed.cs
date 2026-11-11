using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CivicLens.Tests.Fixtures;
using Npgsql;

namespace CivicLens.Tests.PublicApi.Votes;

/// <summary>A published vote as the operational tables hold it. The public views read these rows.</summary>
internal sealed record SeededVote(char Id, string VotedOn)
{
    public string Chamber { get; init; } = "House";
    public int Congress { get; init; } = 119;
    public int Session { get; init; } = 1;
    public int RollCall { get; init; } = 1;
    public string Kind { get; init; } = "Passage";
    public string Result { get; init; } = $"Passed {Id}";
    public string? BillType { get; init; } = "hr";
    public int? BillNumber { get; init; } = 1;
    public string TextOrigin { get; init; } = "Model";
    public string[] Casts { get; init; } = ["Yea", "Yea", "Nay"];
    public string[]? PersonIds { get; init; }
    public string[] Issues { get; init; } = [];
    public string[] UntrackedIssues { get; init; } = [];
    public int FirstRelease { get; init; } = 1;
    public int? LastRelease { get; init; }

    /// <summary>The revision that the record state marks active. A value above 1 seeds the earlier revisions too.</summary>
    public int ActiveRevision { get; init; } = 1;

    /// <summary>The tracked issues of revision 1 when <see cref="ActiveRevision"/> is above 1. <see cref="Issues"/> then belongs to the active revision.</summary>
    public string[] EarlierIssues { get; init; } = [];

    public string RecordId { get; init; } = new(Id, 64);
}

internal static class ViewsSeed
{

    /// <summary>Makes the release the only active one. It adds the release row when none exists.</summary>
    public static Task ActivateReleaseAsync(string database, int release, DateTimeOffset publishedAt, int recordCount = 1) => ExecuteAsync(database, $"""
        UPDATE publication_releases SET is_active = false;
        INSERT INTO publication_releases (release_number, directory_name, actor_subject, published_at_utc_ticks, record_count, is_active)
        VALUES ({release}, '{release:000000}-{new string('0', 31)}1', 'test', {publishedAt.UtcTicks.ToString(CultureInfo.InvariantCulture)}, {recordCount}, true)
        ON CONFLICT (release_number) DO UPDATE SET is_active = true;
        """);

    public static Task AddVotesAsync(string database, params SeededVote[] votes) => AddVotesAsync(database, false, votes);

    public static Task AddVotesBeforeMemberTotalsAsync(string database, params SeededVote[] votes) => AddVotesAsync(database, true, votes);

    private static async Task AddVotesAsync(string database, bool beforeMemberTotals, SeededVote[] votes)
    {
        foreach (var vote in votes) await ExecuteAsync(database, Insert(vote, beforeMemberTotals));
    }

    private static string Insert(SeededVote vote, bool beforeMemberTotals)
    {
        var headerColumns = beforeMemberTotals ? "member_count" : VoteMemberListSeed.Columns;
        var headerValues = beforeMemberTotals ? vote.Casts.Length.ToString(CultureInfo.InvariantCulture) : VoteMemberListSeed.Values(vote.Casts);
        var source = vote.Chamber == "House" ? "CongressGov" : "Voteview";
        var bill = vote.BillType is null ? "NULL, NULL" : $"'{vote.BillType}', {vote.BillNumber}";
        var last = vote.LastRelease?.ToString(CultureInfo.InvariantCulture) ?? "NULL";
        return $"""
            SET session_replication_role = replica;
            INSERT INTO publication_record_states (kind, record_id, first_release, revision_number, last_release)
              VALUES ('vote', '{vote.RecordId}', {vote.FirstRelease}, {vote.ActiveRevision}, {last});
            {string.Join('\n', Enumerable.Range(1, vote.ActiveRevision).Select(revision => Revision(vote, revision, headerColumns, headerValues, source, bill)))}
            {string.Join('\n', vote.EarlierIssues.Select((issue, index) => IssueRow(vote, 1, issue, index, true)))}
            {string.Join('\n', vote.Issues.Select((issue, index) => IssueRow(vote, vote.ActiveRevision, issue, index, true)))}
            {string.Join('\n', vote.UntrackedIssues.Select((issue, index) => IssueRow(vote, vote.ActiveRevision, issue, vote.Issues.Length + index, false)))}
            """;
    }

    private static string Revision(SeededVote vote, int revision, string headerColumns, string headerValues, string source, string bill)
    {
        var evidence = Evidence(vote, revision);
        var list = MemberListId(vote.RecordId, revision);
        var details = Escape(evidence.DetailText);
        var members = Escape(evidence.MemberText);
        var question = $"Question: On {vote.Id}";
        var result = $"Result: {vote.Result}";
        return $"""
            INSERT INTO vote_member_lists (list_id, {headerColumns}, created_at_utc_ticks) VALUES ('{list}', {headerValues}, 1);
            INSERT INTO vote_revisions (record_id, revision_number, author_subject, author_kind, created_at_utc_ticks,
                headline, summary, text_origin, source_id, vote_source, chamber, congress, session_number, roll_call_number, candidate_id,
                candidate_update_utc_ticks, voted_on, voted_on_citation_extraction_id, voted_on_citation_start, voted_on_citation_length,
                kind, kind_citation_extraction_id, kind_citation_start, kind_citation_length, member_list_id,
                issue_revision_id, issue_candidates_recorded, bill_type, bill_number, question, result)
            VALUES ('{vote.RecordId}', {revision}, 'test', 'Person', 1, 'Headline {vote.Id}', 'Summary {vote.Id}', '{vote.TextOrigin}', 'src', '{source}',
                '{vote.Chamber}', {vote.Congress}, {vote.Session}, {vote.RollCall}, 'cand', 1, '{vote.VotedOn}', '{evidence.DetailId}',
                {Offset(evidence.DetailText, $"Started: {vote.VotedOn}T12:00:00Z")}, {DateQuote(vote).Length}, '{vote.Kind}',
                {(vote.Kind == "Other" ? "NULL, NULL, NULL" : $"'{evidence.DetailId}', {Offset(evidence.DetailText, question)}, {question.Length}")},
                '{list}', '{new string('a', 64)}', true, {bill}, 'On {vote.Id}', '{Escape(vote.Result)}');
            INSERT INTO vote_revision_facts (record_id, revision_number, "position", fact_text, citation_extraction_id, citation_start, citation_length)
              VALUES ('{vote.RecordId}', {revision}, 0, '{Escape(question)}', '{evidence.DetailId}', {Offset(evidence.DetailText, question)}, {question.Length}),
                     ('{vote.RecordId}', {revision}, 1, '{Escape(result)}', '{evidence.DetailId}', {Offset(evidence.DetailText, result)}, {result.Length});
            INSERT INTO collection_attempts (attempt_id, source_id, requested_url, final_url, observed_at_utc_ticks,
                outcome, has_response, response_status_code, response_content_encodings, has_sent_validators,
                capture_sha256, elapsed_milliseconds)
            VALUES ('attempt-{evidence.DetailId}', 'vote-test', 'https://requested.example/{vote.RecordId}/detail',
                'https://final.example/{vote.RecordId}/detail', 1, 'captured', true, 200, ARRAY[]::text[], false, REPEAT('b', 64), 0),
                   ('attempt-{evidence.MemberId}', 'vote-test', 'https://requested.example/{vote.RecordId}/members',
                'https://final.example/{vote.RecordId}/members', 1, 'captured', true, 200, ARRAY[]::text[], false, REPEAT('b', 64), 0);
            INSERT INTO document_extractions (extraction_id, attempt_id, extraction_version, text, text_sha256)
            VALUES ('{evidence.DetailId}', 'attempt-{evidence.DetailId}', 'test', '{details}', REPEAT('c', 64)),
                   ('{evidence.MemberId}', 'attempt-{evidence.MemberId}', 'test', '{members}', REPEAT('d', 64));
            {string.Join('\n', vote.Casts.Select((cast, index) => $"""
                INSERT INTO vote_member_list_entries (list_id, "position", person_id, bioguide_id, vote_cast,
                    citation_extraction_id, citation_start, citation_length)
                VALUES ('{list}', {index}, '{Escape(vote.PersonIds?[index] ?? $"p{index}")}', 'B{index:000000}', '{Escape(cast)}',
                    '{evidence.MemberId}', {Offset(evidence.MemberText, MemberQuote(vote, index))}, {MemberQuote(vote, index).Length});
                """))}
            """;
    }

    private static EvidenceFixture Evidence(SeededVote vote, int revision)
    {
        var detail = $"Started: {vote.VotedOn}T12:00:00Z\nQuestion: On {vote.Id}\nResult: {vote.Result}\n";
        var memberLines = vote.Casts.Select((_, index) => MemberQuote(vote, index) + "\n");
        return new(ExtractionId(vote.RecordId, revision, "detail"), ExtractionId(vote.RecordId, revision, "members"), detail, string.Concat(memberLines));
    }

    private static string DateQuote(SeededVote vote) => $"Started: {vote.VotedOn}T12:00:00Z";

    private static string MemberQuote(SeededVote vote, int index) =>
        $"Member: {vote.PersonIds?[index] ?? $"p{index}"}, Official {index}, I, ZZ, {vote.Casts[index]}";

    private static int Offset(string text, string quote) => text.IndexOf(quote, StringComparison.Ordinal);

    internal static string MemberListId(string recordId, int revision) =>
        revision == 1 ? recordId : ExtractionId(recordId, revision, "member-list");

    internal static string ExtractionId(string recordId, int revision, string source) => Convert.ToHexStringLower(
        SHA256.HashData(Encoding.UTF8.GetBytes($"{recordId}:{revision}:{source}")));

    private static string Escape(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    private sealed record EvidenceFixture(string DetailId, string MemberId, string DetailText, string MemberText);

    private static string IssueRow(SeededVote vote, int revision, string issue, int position, bool tracked) => $"""
        INSERT INTO vote_revision_issues (record_id, revision_number, issue_id, position, is_tracked)
        VALUES ('{vote.RecordId}', {revision}, '{issue}', {position}, {(tracked ? "true" : "false")});
        """;

    internal static async Task ExecuteAsync(string database, string sql)
    {
        await using var connection = new NpgsqlConnection(database);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
