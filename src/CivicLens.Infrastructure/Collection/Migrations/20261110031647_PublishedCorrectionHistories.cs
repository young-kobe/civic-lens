using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations
{
    /// <inheritdoc />
    public partial class PublishedCorrectionHistories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "published_correction_histories",
                columns: table => new
                {
                    history_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    encoding_version = table.Column<int>(type: "integer", nullable: false),
                    correction_count = table.Column<short>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_published_correction_histories", x => x.history_id);
                    table.CheckConstraint("ck_published_correction_histories_correction_count", "correction_count BETWEEN 0 AND 62");
                    table.CheckConstraint("ck_published_correction_histories_encoding_version", "encoding_version = 1");
                    table.CheckConstraint("ck_published_correction_histories_history_id", "history_id ~ '^[0-9a-f]{64}$'");
                });

            migrationBuilder.CreateTable(
                name: "published_correction_history_entries",
                columns: table => new
                {
                    history_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    position = table.Column<short>(type: "smallint", nullable: false),
                    replaced_revision_number = table.Column<int>(type: "integer", nullable: false),
                    published_at_utc_ticks = table.Column<long>(type: "bigint", nullable: false),
                    published_offset_minutes = table.Column<short>(type: "smallint", nullable: false),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    facts_unchanged = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_published_correction_history_entries", x => new { x.history_id, x.position });
                    table.CheckConstraint("ck_published_correction_history_entries_local_ticks", "published_at_utc_ticks + published_offset_minutes::bigint * 600000000 BETWEEN 0 AND 3155378975999999999");
                    table.CheckConstraint("ck_published_correction_history_entries_note", "note IS NULL OR length(note) <= 500");
                    table.CheckConstraint("ck_published_correction_history_entries_note_facts_unchanged", "note IS NULL OR NOT facts_unchanged");
                    table.CheckConstraint("ck_published_correction_history_entries_position", "position BETWEEN 0 AND 61");
                    table.CheckConstraint("ck_published_correction_history_entries_published_at_utc_ticks", "published_at_utc_ticks BETWEEN 0 AND 3155378975999999999");
                    table.CheckConstraint("ck_published_correction_history_entries_published_offset_minut~", "published_offset_minutes BETWEEN -840 AND 840");
                    table.CheckConstraint("ck_published_correction_history_entries_replaced_revision_numb~", "replaced_revision_number >= 1");
                    table.ForeignKey(
                        name: "FK_published_correction_history_entries_published_correction_h~",
                        column: x => x.history_id,
                        principalTable: "published_correction_histories",
                        principalColumn: "history_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "release_correction_histories",
                columns: table => new
                {
                    release_number = table.Column<int>(type: "integer", nullable: false),
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    record_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    revision_number = table.Column<int>(type: "integer", nullable: false),
                    history_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_release_correction_histories", x => new { x.release_number, x.kind, x.record_id });
                    table.CheckConstraint("ck_release_correction_histories_history_id", "history_id ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("ck_release_correction_histories_kind", "kind IN ('documentChange', 'statement', 'action', 'brief', 'vote', 'relationship', 'executiveAction')");
                    table.CheckConstraint("ck_release_correction_histories_record_id", "record_id ~ CASE WHEN kind = 'documentChange' THEN '^[0-9a-f]{32}$' ELSE '^[0-9a-f]{64}$' END");
                    table.CheckConstraint("ck_release_correction_histories_revision_number", "revision_number >= 1");
                    table.ForeignKey(
                        name: "FK_release_correction_histories_publication_releases_release_n~",
                        column: x => x.release_number,
                        principalTable: "publication_releases",
                        principalColumn: "release_number",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_release_correction_histories_published_correction_histories~",
                        column: x => x.history_id,
                        principalTable: "published_correction_histories",
                        principalColumn: "history_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_release_correction_histories_published_entries_kind_record_~",
                        columns: x => new { x.kind, x.record_id, x.revision_number },
                        principalTable: "published_entries",
                        principalColumns: new[] { "kind", "record_id", "revision_number" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_release_correction_histories_history_id",
                table: "release_correction_histories",
                column: "history_id");

            migrationBuilder.CreateIndex(
                name: "IX_release_correction_histories_kind_record_id_revision_number",
                table: "release_correction_histories",
                columns: new[] { "kind", "record_id", "revision_number" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "published_correction_history_entries");

            migrationBuilder.DropTable(
                name: "release_correction_histories");

            migrationBuilder.DropTable(
                name: "published_correction_histories");
        }
    }
}
