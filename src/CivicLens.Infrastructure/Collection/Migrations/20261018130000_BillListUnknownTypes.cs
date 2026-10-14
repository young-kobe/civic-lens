using CivicLens.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations;

[DbContext(typeof(CollectionAttemptDbContext))]
[Migration("20261018130000_BillListUnknownTypes")]
public partial class BillListUnknownTypes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "unknown_type_count",
            table: "bill_stream_pages",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddCheckConstraint(
            name: "ck_bill_stream_pages_unknown_types",
            table: "bill_stream_pages",
            sql: "unknown_type_count >= 0");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "ck_bill_stream_pages_unknown_types",
            table: "bill_stream_pages");

        migrationBuilder.DropColumn(
            name: "unknown_type_count",
            table: "bill_stream_pages");
    }
}
