using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations
{
    /// <inheritdoc />
    public partial class DropPublicationRecordsJson : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("LOCK TABLE publication_releases IN SHARE ROW EXCLUSIVE MODE");

            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (
                        WITH positions AS (
                            SELECT release_number, row_number() OVER (ORDER BY release_number) AS position, records_json
                            FROM publication_releases),
                        entries AS (
                            SELECT DISTINCT p.release_number, p.position,
                                COALESCE(e ->> 'kind', 'documentChange') AS kind, COALESCE(e ->> 'recordId', e ->> 'draftId') AS record_id,
                                (e ->> 'revisionNumber')::int AS revision_number
                            FROM positions p CROSS JOIN LATERAL jsonb_array_elements(p.records_json::jsonb) e),
                        islands AS (
                            SELECT *, position - row_number() OVER (PARTITION BY kind, record_id, revision_number ORDER BY position) AS island
                            FROM entries),
                        expected AS (
                            SELECT kind, record_id, revision_number, min(release_number) AS first_release,
                                CASE WHEN max(release_number) = (SELECT max(release_number) FROM publication_releases) THEN NULL
                                    ELSE max(release_number) END AS last_release
                            FROM islands
                            GROUP BY kind, record_id, revision_number, island)
                        (SELECT kind, record_id, revision_number, first_release, last_release FROM expected
                            EXCEPT SELECT kind, record_id, revision_number, first_release, last_release FROM publication_record_states)
                        UNION ALL
                        (SELECT kind, record_id, revision_number, first_release, last_release FROM publication_record_states
                            EXCEPT SELECT kind, record_id, revision_number, first_release, last_release FROM expected)
                    ) THEN
                        RAISE EXCEPTION 'Cannot drop records_json: publication_record_states differs from the records of the releases. Do not drop the column until the table matches.';
                    END IF;
                END $$;
                """);

            migrationBuilder.DropColumn(
                name: "records_json",
                table: "publication_releases");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                LOCK TABLE publication_releases IN SHARE ROW EXCLUSIVE MODE;
                ALTER TABLE publication_releases ADD COLUMN records_json text;
                UPDATE publication_releases r SET records_json = COALESCE((
                    SELECT jsonb_agg(jsonb_build_object('kind', s.kind, 'recordId', s.record_id, 'revisionNumber', s.revision_number)
                        ORDER BY s.kind, s.record_id)
                    FROM publication_record_states s
                    WHERE s.first_release <= r.release_number AND (s.last_release IS NULL OR s.last_release >= r.release_number))::text, '[]');
                ALTER TABLE publication_releases ALTER COLUMN records_json SET NOT NULL;
                """);
        }
    }
}
