using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations
{
    /// <inheritdoc />
    public partial class PublicationRecordStates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("LOCK TABLE publication_releases");

            migrationBuilder.CreateTable(
                name: "publication_record_states",
                columns: table => new
                {
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    record_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    revision_number = table.Column<int>(type: "integer", nullable: false),
                    first_release = table.Column<int>(type: "integer", nullable: false),
                    last_release = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.CheckConstraint("ck_publication_record_states_range", "last_release IS NULL OR last_release >= first_release");
                    table.ForeignKey(
                        name: "FK_publication_record_states_publication_releases_first_release",
                        column: x => x.first_release,
                        principalTable: "publication_releases",
                        principalColumn: "release_number",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_publication_record_states_publication_releases_last_release",
                        column: x => x.last_release,
                        principalTable: "publication_releases",
                        principalColumn: "release_number",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_publication_record_states_first_release",
                table: "publication_record_states",
                column: "first_release",
                filter: "last_release IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_publication_record_states_kind_record_id_first_release",
                table: "publication_record_states",
                columns: new[] { "kind", "record_id", "first_release" },
                unique: true)
                .Annotation("Npgsql:IndexInclude", new[] { "last_release", "revision_number" });

            migrationBuilder.CreateIndex(
                name: "IX_publication_record_states_last_release",
                table: "publication_record_states",
                column: "last_release",
                filter: "last_release IS NOT NULL");

            migrationBuilder.Sql("""
                INSERT INTO publication_record_states (kind, record_id, revision_number, first_release, last_release)
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
                    FROM entries)
                SELECT kind, record_id, revision_number, min(release_number),
                    CASE WHEN max(release_number) = (SELECT max(release_number) FROM publication_releases) THEN NULL
                        ELSE max(release_number) END
                FROM islands
                GROUP BY kind, record_id, revision_number, island
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "publication_record_states");
        }
    }
}
