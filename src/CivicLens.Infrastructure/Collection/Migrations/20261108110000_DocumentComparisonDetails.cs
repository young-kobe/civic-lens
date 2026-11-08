using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations;

public partial class DocumentComparisonDetails : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(name: "hunk_count", table: "document_comparisons", type: "integer", nullable: true);
        migrationBuilder.AddCheckConstraint(name: "ck_document_comparisons_hunk_count", table: "document_comparisons", sql: "hunk_count IS NULL OR (hunk_count >= 0 AND has_changes = (hunk_count > 0))");
        migrationBuilder.CreateTable(
            name: "document_comparison_hunks",
            columns: table => new
            {
                comparison_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                position = table.Column<int>(type: "integer", nullable: false),
                before_start = table.Column<int>(type: "integer", nullable: false),
                before_length = table.Column<int>(type: "integer", nullable: false),
                after_start = table.Column<int>(type: "integer", nullable: false),
                after_length = table.Column<int>(type: "integer", nullable: false),
                before_context_start = table.Column<int>(type: "integer", nullable: false),
                before_context_length = table.Column<int>(type: "integer", nullable: false),
                after_context_start = table.Column<int>(type: "integer", nullable: false),
                after_context_length = table.Column<int>(type: "integer", nullable: false),
                word_edit_count = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_document_comparison_hunks", x => new { x.comparison_id, x.position });
                table.CheckConstraint("ck_document_comparison_hunks_position", "position >= 0");
                table.CheckConstraint("ck_document_comparison_hunks_word_edit_count", "word_edit_count >= 0");
                table.CheckConstraint("ck_document_comparison_hunks_spans", "before_start >= 0 AND before_length >= 0 AND after_start >= 0 AND after_length >= 0 AND before_context_start >= 0 AND before_context_length >= 0 AND after_context_start >= 0 AND after_context_length >= 0");
                table.ForeignKey("FK_document_comparison_hunks_document_comparisons_comparison_id", x => x.comparison_id,
                    principalTable: "document_comparisons", principalColumn: "comparison_id", onDelete: ReferentialAction.Cascade);
            });
        migrationBuilder.CreateTable(
            name: "document_comparison_word_edits",
            columns: table => new
            {
                comparison_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                hunk_position = table.Column<int>(type: "integer", nullable: false),
                position = table.Column<int>(type: "integer", nullable: false),
                before_start = table.Column<int>(type: "integer", nullable: false),
                before_length = table.Column<int>(type: "integer", nullable: false),
                after_start = table.Column<int>(type: "integer", nullable: false),
                after_length = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_document_comparison_word_edits", x => new { x.comparison_id, x.hunk_position, x.position });
                table.CheckConstraint("ck_document_comparison_word_edits_position", "hunk_position >= 0 AND position >= 0");
                table.CheckConstraint("ck_document_comparison_word_edits_spans", "before_start >= 0 AND before_length >= 0 AND after_start >= 0 AND after_length >= 0");
                table.ForeignKey("FK_document_comparison_word_edits_document_comparison_hunks_co~",
                    x => new { x.comparison_id, x.hunk_position }, principalTable: "document_comparison_hunks",
                    principalColumns: new[] { "comparison_id", "position" }, onDelete: ReferentialAction.Cascade);
            });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "document_comparison_word_edits");
        migrationBuilder.DropTable(name: "document_comparison_hunks");
        migrationBuilder.DropCheckConstraint(name: "ck_document_comparisons_hunk_count", table: "document_comparisons");
        migrationBuilder.DropColumn(name: "hunk_count", table: "document_comparisons");
    }
}
