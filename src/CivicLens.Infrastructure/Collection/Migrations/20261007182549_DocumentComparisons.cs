using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations
{
    /// <inheritdoc />
    public partial class DocumentComparisons : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "document_comparisons",
                columns: table => new
                {
                    comparison_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    before_extraction_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    after_extraction_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    algorithm_version = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    settings_version = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    result_json = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document_comparisons", x => x.comparison_id);
                    table.CheckConstraint("ck_document_comparisons_id", "comparison_id ~ '^[0-9a-f]{64}$'");
                    table.ForeignKey(
                        name: "FK_document_comparisons_document_extractions_after_extraction_~",
                        column: x => x.after_extraction_id,
                        principalTable: "document_extractions",
                        principalColumn: "extraction_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_document_comparisons_document_extractions_before_extraction~",
                        column: x => x.before_extraction_id,
                        principalTable: "document_extractions",
                        principalColumn: "extraction_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_document_comparisons_after_extraction_id",
                table: "document_comparisons",
                column: "after_extraction_id");

            migrationBuilder.CreateIndex(
                name: "IX_document_comparisons_before_extraction_id",
                table: "document_comparisons",
                column: "before_extraction_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "document_comparisons");
        }
    }
}
