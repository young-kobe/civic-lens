using CivicLens.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations;

[DbContext(typeof(CollectionAttemptDbContext))]
[Migration("20261013120000_BillStreamCursor")]
public partial class BillStreamCursor : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "bill_stream_cursors",
            columns: table => new
            {
                source_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                last_processed_day = table.Column<DateOnly>(type: "date", nullable: false),
                updated_at_utc_ticks = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_bill_stream_cursors", x => x.source_id);
                table.CheckConstraint("ck_bill_stream_cursors_updated", "updated_at_utc_ticks BETWEEN 0 AND 3155378975999999999");
            });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "bill_stream_cursors");
    }
}
