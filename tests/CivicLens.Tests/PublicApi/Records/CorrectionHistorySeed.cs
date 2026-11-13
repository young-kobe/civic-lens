using System.Globalization;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.PublicApi.Votes;

namespace CivicLens.Tests.PublicApi.Records;

/// <summary>Binds correction histories as the release commit does. A record page reads the history of the shown revision and fails when the binding is missing.</summary>
internal static class CorrectionHistorySeed
{
    /// <summary>Binds an empty history to every record of every release that has no binding yet, so a seeded record page answers with no correction banner.</summary>
    public static Task BindEmptyAsync(string database)
    {
        var historyId = PublishedCorrectionHistoryIdentity.Of([]);
        return ViewsSeed.ExecuteAsync(database, $"""
            SET session_replication_role = replica;
            INSERT INTO published_correction_histories (history_id, encoding_version, correction_count)
            VALUES ('{historyId}', {PublishedCorrectionHistoryIdentity.CurrentVersion}, 0)
            ON CONFLICT (history_id) DO NOTHING;
            INSERT INTO release_correction_histories (release_number, kind, record_id, revision_number, history_id)
            SELECT r.release_number, s.kind, s.record_id, s.revision_number, '{historyId}'
              FROM publication_releases r
              JOIN publication_record_states s
                ON s.first_release <= r.release_number AND (s.last_release IS NULL OR s.last_release >= r.release_number)
            ON CONFLICT DO NOTHING;
            """);
    }

    /// <summary>Binds the corrections, newest first, to the active revision of a seeded record in the release.</summary>
    public static Task BindAsync(string database, int release, PublishedRecordKind kind, string recordId, params PublishedCorrection[] corrections)
    {
        var historyId = PublishedCorrectionHistoryIdentity.Of(corrections);
        var kindText = PublicationProtocol.KindText(kind);
        var entries = corrections.Length == 0 ? "" : $"""
            INSERT INTO published_correction_history_entries (history_id, "position", replaced_revision_number, published_at_utc_ticks, published_offset_minutes, note, facts_unchanged)
            VALUES {string.Join(", ", corrections.Select((entry, position) =>
                $"('{historyId}', {position}, {entry.ReplacedRevisionNumber}, {entry.PublishedAtUtc.UtcTicks.ToString(CultureInfo.InvariantCulture)}, {(int)entry.PublishedAtUtc.Offset.TotalMinutes}, {(entry.Note is null ? "NULL" : $"'{entry.Note.Replace("'", "''", StringComparison.Ordinal)}'")}, {(entry.FactsUnchanged ? "true" : "false")})"))}
            ON CONFLICT DO NOTHING;
            """;
        return ViewsSeed.ExecuteAsync(database, $"""
            SET session_replication_role = replica;
            INSERT INTO published_correction_histories (history_id, encoding_version, correction_count)
            VALUES ('{historyId}', {PublishedCorrectionHistoryIdentity.CurrentVersion}, {corrections.Length})
            ON CONFLICT (history_id) DO NOTHING;
            {entries}
            INSERT INTO release_correction_histories (release_number, kind, record_id, revision_number, history_id)
            SELECT {release}, s.kind, s.record_id, s.revision_number, '{historyId}'
              FROM publication_record_states s WHERE s.kind = '{kindText}' AND s.record_id = '{recordId}';
            """);
    }
}
