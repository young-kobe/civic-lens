using System.Globalization;
using CivicLens.Tests.PublicApi.Votes;

namespace CivicLens.Tests.PublicApi.Home;

/// <summary>A published brief as the operational tables hold it. The public views read these rows.</summary>
internal sealed record SeededBrief(char Id, string WeekStart)
{
    public string Headline { get; init; } = $"Brief {Id}";
    public string TextOrigin { get; init; } = "Model";
    public bool WithColumns { get; init; } = true;
    public int FirstRelease { get; init; } = 1;
    public int? LastRelease { get; init; }

    public string RecordId { get; init; } = new(Id, 64);
}

internal static class HomeSeed
{
    private const long WeekTicks = 7L * 24 * 3600 * 10_000_000;

    public static async Task AddBriefsAsync(string database, params SeededBrief[] briefs)
    {
        foreach (var brief in briefs) await ViewsSeed.ExecuteAsync(database, Insert(brief));
    }

    private static string Insert(SeededBrief brief)
    {
        const string emptyJson = "{}";
        var last = brief.LastRelease?.ToString(CultureInfo.InvariantCulture) ?? "NULL";
        var author = brief.TextOrigin == "Person" ? "'Person', NULL" : $"'Analysis', '{new string(brief.Id, 32)}'";
        var columns = brief.WithColumns
            ? $"'{brief.WeekStart}', 0, {WeekTicks}, 1, 1, '{new string('c', 64)}', '{brief.Headline}', '{brief.TextOrigin}', 0, 0"
            : "NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL";
        return $"""
            SET session_replication_role = replica;
            INSERT INTO publication_record_states (kind, record_id, first_release, revision_number, last_release)
              VALUES ('brief', '{brief.RecordId}', {brief.FirstRelease}, 1, {last});
            INSERT INTO brief_revisions (record_id, revision_number, author_subject, author_kind, run_id, revision_json, created_at_utc_ticks,
                week_start, window_start_utc_ticks, window_end_utc_ticks, first_release_number, cutoff_release_number, fingerprint, headline, text_origin,
                coverage_considered, coverage_sent_to_model)
            VALUES ('{brief.RecordId}', 1, 'test', {author}, '{emptyJson}', 1, {columns});
            """;
    }
}
