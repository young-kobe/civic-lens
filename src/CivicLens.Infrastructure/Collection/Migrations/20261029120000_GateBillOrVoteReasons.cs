using CivicLens.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations;

[DbContext(typeof(CollectionAttemptDbContext))]
[Migration("20261029120000_GateBillOrVoteReasons")]
public partial class GateBillOrVoteReasons : Migration
{
    private const string Name = "ck_statement_analysis_runs_reason";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(name: Name, table: "statement_analysis_runs");
        migrationBuilder.AddCheckConstraint(name: Name, table: "statement_analysis_runs",
            sql: "reason IS NULL OR reason IN ('passed','passed-bill-or-vote','passed-bill-number','no-tracked-issue','over-byte-cap','gate-failed-open')");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(name: Name, table: "statement_analysis_runs");
        migrationBuilder.AddCheckConstraint(name: Name, table: "statement_analysis_runs",
            sql: "reason IS NULL OR reason IN ('passed','no-tracked-issue','over-byte-cap','gate-failed-open')");
    }
}
