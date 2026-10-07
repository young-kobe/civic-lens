using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations
{
    /// <inheritdoc />
    public partial class DocumentExtractions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "document_extractions",
                columns: table => new
                {
                    extraction_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    attempt_id = table.Column<string>(type: "text", nullable: false),
                    parser_version = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    normalization_version = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    text = table.Column<string>(type: "text", nullable: false),
                    text_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document_extractions", x => x.extraction_id);
                    table.CheckConstraint("ck_document_extractions_hashes", "extraction_id ~ '^[0-9a-f]{64}$' AND text_sha256 ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("ck_document_extractions_text_limit", "length(text) <= 2000000");
                    table.CheckConstraint("ck_document_extractions_versions", "length(btrim(parser_version)) > 0 AND length(btrim(normalization_version)) > 0");
                    table.ForeignKey(
                        name: "FK_document_extractions_collection_attempts_attempt_id",
                        column: x => x.attempt_id,
                        principalTable: "collection_attempts",
                        principalColumn: "attempt_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_document_extractions_attempt",
                table: "document_extractions",
                column: "attempt_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "document_extractions");
        }
    }
}
