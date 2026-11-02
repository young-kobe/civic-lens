using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations
{
    /// <inheritdoc />
    public partial class BriefCheck : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_brief_analysis_week",
                table: "brief_analysis");

            migrationBuilder.AddColumn<int>(
                name: "checked_revision_number",
                table: "brief_analysis_runs",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "recommendation",
                table: "brief_analysis_runs",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "brief_check_flags",
                columns: table => new
                {
                    run_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    flag_number = table.Column<int>(type: "integer", nullable: false),
                    record_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    revision_number = table.Column<int>(type: "integer", nullable: false),
                    paragraph_index = table.Column<int>(type: "integer", nullable: false),
                    code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    citation_id = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    note = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_brief_check_flags", x => new { x.run_id, x.flag_number });
                    table.CheckConstraint("ck_brief_check_flags_code", "code IN ('UnsupportedByCitation','Overstated','StatesBillStatus','LoadedWording','OmitsNotableRecord','Other')");
                    table.CheckConstraint("ck_brief_check_flags_note", "char_length(note) BETWEEN 1 AND 300");
                    table.CheckConstraint("ck_brief_check_flags_number", "flag_number BETWEEN 1 AND 24");
                    table.CheckConstraint("ck_brief_check_flags_paragraph", "paragraph_index BETWEEN 0 AND 11");
                    table.ForeignKey(
                        name: "FK_brief_check_flags_brief_analysis_runs_run_id",
                        column: x => x.run_id,
                        principalTable: "brief_analysis_runs",
                        principalColumn: "run_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_brief_check_flags_brief_revisions_record_id_revision_number",
                        columns: x => new { x.record_id, x.revision_number },
                        principalTable: "brief_revisions",
                        principalColumns: new[] { "record_id", "revision_number" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_brief_analysis_runs_record_id_checked_revision_number",
                table: "brief_analysis_runs",
                columns: new[] { "record_id", "checked_revision_number" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_brief_analysis_runs_check",
                table: "brief_analysis_runs",
                sql: "(checked_revision_number IS NULL) = (recommendation IS NULL) AND (checked_revision_number IS NULL OR (checked_revision_number >= 1 AND task = 'brief-check' AND recommendation IN ('ApproveRecommended','NeedsAttention')))");

            migrationBuilder.CreateIndex(
                name: "ux_brief_analysis_week",
                table: "brief_analysis",
                columns: new[] { "week_start", "task" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_brief_check_flags_record_id_revision_number",
                table: "brief_check_flags",
                columns: new[] { "record_id", "revision_number" });

            migrationBuilder.AddForeignKey(
                name: "FK_brief_analysis_runs_brief_revisions_record_id_checked_revis~",
                table: "brief_analysis_runs",
                columns: new[] { "record_id", "checked_revision_number" },
                principalTable: "brief_revisions",
                principalColumns: new[] { "record_id", "revision_number" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_brief_analysis_runs_brief_revisions_record_id_checked_revis~",
                table: "brief_analysis_runs");

            migrationBuilder.DropTable(
                name: "brief_check_flags");

            migrationBuilder.DropIndex(
                name: "IX_brief_analysis_runs_record_id_checked_revision_number",
                table: "brief_analysis_runs");

            migrationBuilder.DropCheckConstraint(
                name: "ck_brief_analysis_runs_check",
                table: "brief_analysis_runs");

            migrationBuilder.DropIndex(
                name: "ux_brief_analysis_week",
                table: "brief_analysis");

            migrationBuilder.DropColumn(
                name: "checked_revision_number",
                table: "brief_analysis_runs");

            migrationBuilder.DropColumn(
                name: "recommendation",
                table: "brief_analysis_runs");

            migrationBuilder.CreateIndex(
                name: "ux_brief_analysis_week",
                table: "brief_analysis",
                column: "week_start",
                unique: true);
        }
    }
}
