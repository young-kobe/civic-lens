using System.Globalization;
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
    public string? BillType { get; init; } = "HR";
    public int? BillNumber { get; init; } = 1;
    public string TextOrigin { get; init; } = "Model";
    public string[] Casts { get; init; } = ["Yea", "Yea", "Nay"];
    public string[] Issues { get; init; } = [];
    public string[] UntrackedIssues { get; init; } = [];
    public int FirstRelease { get; init; } = 1;
    public int? LastRelease { get; init; }

    public string RecordId { get; init; } = new(Id, 64);
}

internal static class ViewsSeed
{

    /// <summary>Makes the release the only active one. It adds the release row when none exists.</summary>
    public static Task ActivateReleaseAsync(string database, int release, DateTimeOffset publishedAt) => ExecuteAsync(database, $"""
        UPDATE publication_releases SET is_active = false;
        INSERT INTO publication_releases (release_number, directory_name, actor_subject, published_at_utc_ticks, record_count, is_active)
        VALUES ({release}, '{release:000000}-{new string('0', 31)}1', 'test', {publishedAt.UtcTicks.ToString(CultureInfo.InvariantCulture)}, 1, true)
        ON CONFLICT (release_number) DO UPDATE SET is_active = true;
        """);

    public static async Task AddVotesAsync(string database, params SeededVote[] votes)
    {
        foreach (var vote in votes) await ExecuteAsync(database, Insert(vote));
    }

    private static string Insert(SeededVote vote)
    {
        var list = vote.RecordId;
        var source = vote.Chamber == "House" ? "CongressGov" : "Voteview";
        var bill = vote.BillType is null ? "NULL, NULL" : $"'{vote.BillType}', {vote.BillNumber}";
        var last = vote.LastRelease?.ToString(CultureInfo.InvariantCulture) ?? "NULL";
        return $"""
            SET session_replication_role = replica;
            INSERT INTO publication_record_states (kind, record_id, first_release, revision_number, last_release)
              VALUES ('vote', '{vote.RecordId}', {vote.FirstRelease}, 1, {last});
            INSERT INTO vote_member_lists (list_id, member_count, created_at_utc_ticks) VALUES ('{list}', {vote.Casts.Length}, 1);
            INSERT INTO vote_revisions (record_id, revision_number, author_subject, author_kind, revision_json, created_at_utc_ticks,
                headline, summary, text_origin, source_id, vote_source, chamber, congress, session_number, roll_call_number, candidate_id,
                candidate_update_utc_ticks, voted_on, voted_on_citation_extraction_id, voted_on_citation_start, voted_on_citation_length,
                kind, member_list_id, issue_revision_id, issue_candidates_recorded, bill_type, bill_number)
            VALUES ('{vote.RecordId}', 1, 'test', 'Person', 'x', 1, 'Headline {vote.Id}', 'Summary {vote.Id}', '{vote.TextOrigin}', 'src', '{source}',
                '{vote.Chamber}', {vote.Congress}, {vote.Session}, {vote.RollCall}, 'cand', 1, '{vote.VotedOn}', 'ext', 0, 1,
                '{vote.Kind}', '{list}', '{new string('a', 64)}', true, {bill});
            INSERT INTO vote_revision_facts (record_id, revision_number, "position", fact_text, citation_extraction_id, citation_start, citation_length)
              VALUES ('{vote.RecordId}', 1, 0, 'Question: On {vote.Id}', 'ext', 0, 1), ('{vote.RecordId}', 1, 1, 'Result: Passed {vote.Id}', 'ext', 0, 1);
            {string.Join('\n', vote.Casts.Select((cast, index) => $"""
                INSERT INTO vote_member_list_entries (list_id, "position", person_id, bioguide_id, vote_cast, citation_extraction_id, citation_start, citation_length)
                VALUES ('{list}', {index}, 'p{index}', 'B{index:000000}', '{cast}', 'ext', 0, 1);
                """))}
            {string.Join('\n', vote.Issues.Select((issue, index) => IssueRow(vote, issue, index, true)))}
            {string.Join('\n', vote.UntrackedIssues.Select((issue, index) => IssueRow(vote, issue, vote.Issues.Length + index, false)))}
            """;
    }

    private static string IssueRow(SeededVote vote, string issue, int position, bool tracked) => $"""
        INSERT INTO vote_revision_issues (record_id, revision_number, issue_id, position, is_tracked)
        VALUES ('{vote.RecordId}', 1, '{issue}', {position}, {(tracked ? "true" : "false")});
        """;

    private static async Task ExecuteAsync(string database, string sql)
    {
        await using var connection = new NpgsqlConnection(database);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
