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
              VALUES ('vote', '{vote.RecordId}', {vote.FirstRelease}, {vote.ActiveRevision}, {last});
            INSERT INTO vote_member_lists (list_id, member_count, created_at_utc_ticks) VALUES ('{list}', {vote.Casts.Length}, 1);
            {string.Join('\n', Enumerable.Range(1, vote.ActiveRevision).Select(revision => Revision(vote, revision, list, source, bill)))}
            {string.Join('\n', vote.Casts.Select((cast, index) => $"""
                INSERT INTO vote_member_list_entries (list_id, "position", person_id, bioguide_id, vote_cast, citation_extraction_id, citation_start, citation_length)
                VALUES ('{list}', {index}, '{vote.PersonIds?[index] ?? $"p{index}"}', 'B{index:000000}', '{cast}', 'ext', 0, 1);
                """))}
            {string.Join('\n', vote.EarlierIssues.Select((issue, index) => IssueRow(vote, 1, issue, index, true)))}
            {string.Join('\n', vote.Issues.Select((issue, index) => IssueRow(vote, vote.ActiveRevision, issue, index, true)))}
            {string.Join('\n', vote.UntrackedIssues.Select((issue, index) => IssueRow(vote, vote.ActiveRevision, issue, vote.Issues.Length + index, false)))}
            """;
    }

    private static string Revision(SeededVote vote, int revision, string list, string source, string bill)
    {
        return $"""
            INSERT INTO vote_revisions (record_id, revision_number, author_subject, author_kind, created_at_utc_ticks,
                headline, summary, text_origin, source_id, vote_source, chamber, congress, session_number, roll_call_number, candidate_id,
                candidate_update_utc_ticks, voted_on, voted_on_citation_extraction_id, voted_on_citation_start, voted_on_citation_length,
                kind, member_list_id, issue_revision_id, issue_candidates_recorded, bill_type, bill_number, question, result)
            VALUES ('{vote.RecordId}', {revision}, 'test', 'Person', 1, 'Headline {vote.Id}', 'Summary {vote.Id}', '{vote.TextOrigin}', 'src', '{source}',
                '{vote.Chamber}', {vote.Congress}, {vote.Session}, {vote.RollCall}, 'cand', 1, '{vote.VotedOn}', 'ext', 0, 1,
                '{vote.Kind}', '{list}', '{new string('a', 64)}', true, {bill}, 'On {vote.Id}', '{vote.Result}');
            INSERT INTO vote_revision_facts (record_id, revision_number, "position", fact_text, citation_extraction_id, citation_start, citation_length)
              VALUES ('{vote.RecordId}', {revision}, 0, 'Question: On {vote.Id}', 'ext', 0, 1), ('{vote.RecordId}', {revision}, 1, 'Result: {vote.Result}', 'ext', 0, 1);
            """;
    }

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
