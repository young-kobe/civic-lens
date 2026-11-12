using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations
{
    /// <inheritdoc />
    public partial class ExecutiveActionTexts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "body_html_url",
                table: "executive_action_documents",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "executive_action_texts",
                columns: table => new
                {
                    source_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    document_number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    extraction_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    chars_total = table.Column<int>(type: "integer", nullable: false),
                    chars_read = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_executive_action_texts", x => new { x.source_id, x.document_number });
                    table.CheckConstraint("ck_executive_action_texts_chars", "chars_read >= 1 AND chars_read <= 60000 AND chars_read <= chars_total AND (chars_read = chars_total OR chars_total > 60000)");
                    table.ForeignKey(
                        name: "FK_executive_action_texts_document_extractions_extraction_id",
                        column: x => x.extraction_id,
                        principalTable: "document_extractions",
                        principalColumn: "extraction_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_executive_action_texts_executive_action_documents_source_id~",
                        columns: x => new { x.source_id, x.document_number },
                        principalTable: "executive_action_documents",
                        principalColumns: new[] { "source_id", "document_number" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_executive_action_texts_extraction_id",
                table: "executive_action_texts",
                column: "extraction_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "executive_action_texts");

            migrationBuilder.DropColumn(
                name: "body_html_url",
                table: "executive_action_documents");
        }
    }
}
