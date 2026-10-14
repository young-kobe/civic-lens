using CivicLens.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations;

[DbContext(typeof(CollectionAttemptDbContext))]
[Migration("20261017120000_BillSummaryCount")]
public partial class BillSummaryCount : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "summary_count",
            table: "bill_gates",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddCheckConstraint(
            name: "ck_bill_gates_summary_count",
            table: "bill_gates",
            sql: "summary_count >= 0");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "ck_bill_gates_summary_count",
            table: "bill_gates");

        migrationBuilder.DropColumn(
            name: "summary_count",
            table: "bill_gates");
    }
}
