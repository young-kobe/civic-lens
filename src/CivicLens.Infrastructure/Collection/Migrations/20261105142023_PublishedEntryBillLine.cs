using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations
{
    /// <inheritdoc />
    public partial class PublishedEntryBillLine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "bill_line",
                table: "published_entries",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_published_entries_bill_line",
                table: "published_entries",
                sql: "bill_line IS NULL OR (kind = 'action' AND length(btrim(bill_line)) > 0 AND length(bill_line) <= 200)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_published_entries_bill_line",
                table: "published_entries");

            migrationBuilder.DropColumn(
                name: "bill_line",
                table: "published_entries");
        }
    }
}
