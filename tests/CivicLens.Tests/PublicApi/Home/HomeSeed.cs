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

internal sealed record SeededExecutiveAction(char Id, string Kind, string SignedOn, string PublishedOn)
{
    public string Title { get; init; } = $"Executive title {Id}";
    public int ActiveRevision { get; init; } = 1;
    public bool AddUnpublishedRevision { get; init; }
    public int FirstRelease { get; init; } = 1;
    public string RecordId { get; init; } = new(Id, 64);
}

internal static class HomeSeed
{
    private const long WeekTicks = 7L * 24 * 3600 * 10_000_000;

    public static async Task AddBriefsAsync(string database, params SeededBrief[] briefs)
    {
        foreach (var brief in briefs) await ViewsSeed.ExecuteAsync(database, Insert(brief));
    }

    public static async Task AddExecutiveActionsAsync(string database, params SeededExecutiveAction[] actions)
    {
        foreach (var action in actions) await ViewsSeed.ExecuteAsync(database, Insert(action));
    }

    private static string Insert(SeededExecutiveAction action)
    {
        var revisions = string.Join('\n', Enumerable.Range(1, action.ActiveRevision + (action.AddUnpublishedRevision ? 1 : 0)).Select(revision => $"""
            INSERT INTO executive_action_revisions (record_id, revision_number, revision_json, created_at_utc_ticks, author_kind, author_subject, code_version,
                source_id, document_number, kind, title, signed_on, published_on, federal_register_citation, president_name, person_id, document_url)
            VALUES ('{action.RecordId}', {revision}, 'x', 1, 'Code', 'civic-lens:code', '{new string('c', 40)}', 'fr', '2026-{action.Id}{revision:0000}',
                '{action.Kind}', '{(revision == action.ActiveRevision ? action.Title : revision < action.ActiveRevision ? $"Old title {action.Id}" : "Unpublished revision")}',
                '{action.SignedOn}', '{action.PublishedOn}', '91 FR 1', 'Jane Roe', 'president', 'https://fr.example/1');
            """));
        return $"""
            SET session_replication_role = replica;
            INSERT INTO publication_record_states (kind, record_id, first_release, revision_number, last_release)
              VALUES ('executiveAction', '{action.RecordId}', {action.FirstRelease}, {action.ActiveRevision}, NULL);
            {revisions}
            """;
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
