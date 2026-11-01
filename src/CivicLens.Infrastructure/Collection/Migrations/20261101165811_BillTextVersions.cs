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
                name: "text_package_id",
                table: "bill_gates",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

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
                    chars_read = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bill_text_versions", x => new { x.congress, x.bill_type, x.bill_number, x.package_id });
                    table.CheckConstraint("ck_bill_text_versions_bill", "congress >= 1 AND bill_number >= 1");
                    table.CheckConstraint("ck_bill_text_versions_chars", "chars_read >= 1 AND chars_read <= 60000 AND chars_read <= chars_total AND (chars_read = chars_total OR chars_total > 60000)");
                    table.CheckConstraint("ck_bill_text_versions_package", "package_id ~ ('^BILLS-' || congress || bill_type || bill_number || '[a-z]+$') OR package_id ~ ('^PLAW-' || congress || '(publ|pvtl)[0-9]+$')");
                    table.CheckConstraint("ck_bill_text_versions_type", "version_type = btrim(version_type) AND char_length(version_type) >= 1");
                    table.CheckConstraint("ck_bill_text_versions_url", "source_url LIKE 'https://www.congress.gov/%'");
                    table.ForeignKey(
                        name: "FK_bill_text_versions_document_extractions_extraction_id",
                        column: x => x.extraction_id,
                        principalTable: "document_extractions",
                        principalColumn: "extraction_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_bill_gates_congress_bill_type_bill_number_text_package_id",
                table: "bill_gates",
                columns: new[] { "congress", "bill_type", "bill_number", "text_package_id" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_bill_gates_text_package",
                table: "bill_gates",
                sql: "COALESCE(text_state = 'Read', false) = (text_package_id IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_bill_gates_text_state",
                table: "bill_gates",
                sql: "text_state IN ('NoText','Read')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_bill_gates_text_without_summary",
                table: "bill_gates",
                sql: "text_state IS NULL OR summary_count = 0");

            migrationBuilder.CreateIndex(
                name: "IX_bill_text_versions_extraction_id",
                table: "bill_text_versions",
                column: "extraction_id");

            migrationBuilder.AddForeignKey(
                name: "FK_bill_gates_bill_text_versions_congress_bill_type_bill_numbe~",
                table: "bill_gates",
                columns: new[] { "congress", "bill_type", "bill_number", "text_package_id" },
                principalTable: "bill_text_versions",
                principalColumns: new[] { "congress", "bill_type", "bill_number", "package_id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_bill_gates_bill_text_versions_congress_bill_type_bill_numbe~",
                table: "bill_gates");

            migrationBuilder.DropTable(
                name: "bill_text_versions");

            migrationBuilder.DropIndex(
                name: "IX_bill_gates_congress_bill_type_bill_number_text_package_id",
                table: "bill_gates");

            migrationBuilder.DropCheckConstraint(
                name: "ck_bill_gates_text_package",
                table: "bill_gates");

            migrationBuilder.DropCheckConstraint(
                name: "ck_bill_gates_text_state",
                table: "bill_gates");

            migrationBuilder.DropCheckConstraint(
                name: "ck_bill_gates_text_without_summary",
                table: "bill_gates");

            migrationBuilder.DropColumn(
                name: "text_package_id",
                table: "bill_gates");

            migrationBuilder.DropColumn(
                name: "text_state",
                table: "bill_gates");
        }
    }
}
