using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations
{
    public partial class PublicApiZipDistricts : Migration
    {
        // The CTEs repeat RosterSnapshotRules.ReadLatestSnapshotAttemptIdAsync for each source that owns ZCTA rows.
        // PublicApiViewsTests compares the view with PostgresMemberRosterStore.ReadLatestAsync, which applies that method.
        private const string UpSql = """
            CREATE VIEW public_api.zip_districts WITH (security_barrier = true) AS
            WITH district_sources AS (
              SELECT DISTINCT a.source_id
                FROM public.zcta_districts z
                JOIN public.collection_attempts a ON a.attempt_id = z.attempt_id),
            snapshots AS (
              SELECT a.attempt_id, a.source_id, a.capture_sha256, a.observed_at_utc_ticks
                FROM public.collection_attempts a
                JOIN public.roster_snapshots s ON s.attempt_id = a.attempt_id
               WHERE a.source_id IN (SELECT source_id FROM district_sources)),
            newest_capture AS (
              SELECT DISTINCT ON (a.source_id) a.source_id, a.capture_sha256
                FROM public.collection_attempts a
               WHERE a.source_id IN (SELECT source_id FROM district_sources)
                 AND EXISTS (SELECT 1 FROM snapshots s WHERE s.source_id = a.source_id AND s.capture_sha256 = a.capture_sha256)
               ORDER BY a.source_id, a.observed_at_utc_ticks DESC, a.attempt_id DESC),
            newest_snapshot AS (
              SELECT DISTINCT ON (s.source_id) s.attempt_id
                FROM snapshots s
                JOIN newest_capture c ON c.source_id = s.source_id AND c.capture_sha256 = s.capture_sha256
               ORDER BY s.source_id, s.observed_at_utc_ticks, s.attempt_id)
            SELECT z.zcta, z.state, z.district, z.land_area_square_meters,
                   z.land_area_square_meters::numeric / sum(z.land_area_square_meters) OVER (PARTITION BY z.attempt_id, z.zcta) AS land_share
              FROM public.zcta_districts z
              JOIN newest_snapshot n ON n.attempt_id = z.attempt_id;

            ALTER VIEW public_api.zip_districts OWNER TO civic_lens_public_owner;
            """;

        private const string DownSql = """
            DROP VIEW public_api.zip_districts;
            REVOKE SELECT ON zcta_districts, roster_snapshots, collection_attempts FROM civic_lens_public_owner;
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(UpSql);

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(DownSql);
    }
}
