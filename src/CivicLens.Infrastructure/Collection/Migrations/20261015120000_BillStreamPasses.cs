using CivicLens.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations;

[DbContext(typeof(CollectionAttemptDbContext))]
[Migration("20261015120000_BillStreamPasses")]
public partial class BillStreamPasses : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<DateOnly>(
            name: "last_processed_day",
            table: "bill_stream_cursors",
            type: "date",
            nullable: true,
            oldClrType: typeof(DateOnly),
            oldType: "date");

        migrationBuilder.AddColumn<int>(
            name: "pass_admitted_bills",
            table: "bill_stream_cursors",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddCheckConstraint(
            name: "ck_bill_stream_cursors_pass_admitted_bills",
            table: "bill_stream_cursors",
            sql: "pass_admitted_bills >= 0");

        migrationBuilder.CreateTable(
            name: "bill_stream_pages",
            columns: table => new
            {
                list_job_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                recorded_at_utc_ticks = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_bill_stream_pages", x => x.list_job_id);
                table.CheckConstraint("ck_bill_stream_pages_recorded", "recorded_at_utc_ticks BETWEEN 0 AND 3155378975999999999");
                table.ForeignKey(
                    name: "FK_bill_stream_pages_collection_jobs_list_job_id",
                    column: x => x.list_job_id,
                    principalTable: "collection_jobs",
                    principalColumn: "job_id",
                    onDelete: ReferentialAction.Restrict);
            });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "bill_stream_pages");

        migrationBuilder.DropCheckConstraint(
            name: "ck_bill_stream_cursors_pass_admitted_bills",
            table: "bill_stream_cursors");

        migrationBuilder.DropColumn(
            name: "pass_admitted_bills",
            table: "bill_stream_cursors");

        migrationBuilder.Sql("DELETE FROM bill_stream_cursors WHERE last_processed_day IS NULL");

        migrationBuilder.AlterColumn<DateOnly>(
            name: "last_processed_day",
            table: "bill_stream_cursors",
            type: "date",
            nullable: false,
            oldClrType: typeof(DateOnly),
            oldType: "date",
            oldNullable: true);
    }
}
