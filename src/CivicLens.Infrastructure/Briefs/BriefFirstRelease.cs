namespace CivicLens.Infrastructure.Briefs;

internal static class BriefFirstRelease
{
    public const string WantedFirstPublishedCtes = """
        wanted AS (SELECT k.kind, k.record_id FROM unnest({0}, {1}) AS k(kind, record_id)),
        first_published AS (
            SELECT w.kind, w.record_id,
                   (SELECT r.published_at_utc_ticks FROM publication_releases r
                    WHERE r.release_number = (SELECT min(s.first_release) FROM publication_record_states s
                                              WHERE s.kind = w.kind AND s.record_id = w.record_id)) AS ticks
            FROM wanted w)
        """;
}
