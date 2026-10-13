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
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
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
