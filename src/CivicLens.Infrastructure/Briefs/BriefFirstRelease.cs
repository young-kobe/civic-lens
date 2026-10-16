using CivicLens.Infrastructure.Review;

namespace CivicLens.Infrastructure.Briefs;

internal static class BriefFirstRelease
{
    public static readonly string WantedFirstPublishedCtes = $$"""
        wanted AS (SELECT k.kind, k.record_id FROM unnest({0}, {1}) AS k(kind, record_id)),
        first_published AS (
            SELECT w.kind, w.record_id,
                   (SELECT r.published_at_utc_ticks FROM publication_releases r
                    WHERE EXISTS (SELECT 1 FROM (
                        SELECT {{ReviewQueries.ReleaseRecordColumns}} FROM jsonb_array_elements(r.records_json::jsonb) e) listed
                        WHERE lower(listed.kind) = lower(w.kind) AND listed.record_id = w.record_id)
                    ORDER BY r.release_number LIMIT 1) AS ticks
            FROM wanted w)
        """;
}
