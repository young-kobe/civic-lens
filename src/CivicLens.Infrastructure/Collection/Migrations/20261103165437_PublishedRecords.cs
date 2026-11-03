using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations
{
    /// <inheritdoc />
    public partial class PublishedRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "published_records",
                columns: table => new
                {
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    record_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    first_published_at_utc_ticks = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_published_records", x => new { x.kind, x.record_id });
                    table.CheckConstraint("ck_published_records_first_published_at_utc_ticks", "first_published_at_utc_ticks BETWEEN 0 AND 3155378975999999999");
                    table.CheckConstraint("ck_published_records_kind", "kind IN ('documentChange', 'statement', 'action', 'brief', 'vote', 'relationship', 'executiveAction')");
                    table.CheckConstraint("ck_published_records_record_id", "record_id ~ '^([0-9a-f]{32}|[0-9a-f]{64})$'");
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "published_records");
        }
    }
}
