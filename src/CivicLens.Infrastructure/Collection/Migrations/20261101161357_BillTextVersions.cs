using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations
{
    /// <inheritdoc />
    public partial class BillTextVersions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "text_state",
                table: "bill_gates",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "bill_text_versions",
                columns: table => new
                {
                    congress = table.Column<int>(type: "integer", nullable: false),
                    bill_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    bill_number = table.Column<int>(type: "integer", nullable: false),
                    package_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    version_type = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    version_date = table.Column<DateOnly>(type: "date", nullable: true),
                    source_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    extraction_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    chars_total = table.Column<int>(type: "integer", nullable: false),
                    chars_read = table.Column<int>(type: "integer", nullable: false),
                    content_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bill_text_versions", x => new { x.congress, x.bill_type, x.bill_number, x.package_id });
                    table.CheckConstraint("ck_bill_text_versions_bill", "congress >= 1 AND bill_number >= 1");
                    table.CheckConstraint("ck_bill_text_versions_chars", "chars_total >= 0 AND chars_read >= 0 AND chars_read <= chars_total");
                    table.CheckConstraint("ck_bill_text_versions_hash", "content_sha256 ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("ck_bill_text_versions_package", "package_id ~ '^BILLS-[0-9]+[a-z]+[0-9]+[a-z]+$'");
                    table.CheckConstraint("ck_bill_text_versions_type", "version_type = btrim(version_type) AND char_length(version_type) >= 1");
                    table.ForeignKey(
                        name: "FK_bill_text_versions_document_extractions_extraction_id",
                        column: x => x.extraction_id,
                        principalTable: "document_extractions",
                        principalColumn: "extraction_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_bill_gates_text_state",
                table: "bill_gates",
                sql: "text_state IN ('NoText','Read')");

            migrationBuilder.CreateIndex(
                name: "IX_bill_text_versions_extraction_id",
                table: "bill_text_versions",
                column: "extraction_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bill_text_versions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_bill_gates_text_state",
                table: "bill_gates");

            migrationBuilder.DropColumn(
                name: "text_state",
                table: "bill_gates");
        }
    }
}
