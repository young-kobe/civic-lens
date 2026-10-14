using CivicLens.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations;

[DbContext(typeof(CollectionAttemptDbContext))]
[Migration("20261022140000_AnalysisClaimIndexes")]
public partial class AnalysisClaimIndexes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateIndex(
            name: "ix_document_change_analysis_active_claim",
            table: "document_change_analysis",
            columns: new[] { "task", "created_at_utc_ticks", "comparison_id" },
            filter: "status IN ('Pending','RetryWaiting','WaitingForBudget','Running')");
        migrationBuilder.CreateIndex(
            name: "ix_statement_analysis_active_claim",
            table: "statement_analysis",
            columns: new[] { "task", "created_at_utc_ticks", "candidate_id" },
            filter: "status IN ('Pending','RetryWaiting','WaitingForBudget','Running')");
        migrationBuilder.CreateIndex(
            name: "ix_action_analysis_active_claim",
            table: "action_analysis",
            columns: new[] { "task", "created_at_utc_ticks", "candidate_id" },
            filter: "status IN ('Pending','RetryWaiting','WaitingForBudget','Running')");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "ix_document_change_analysis_active_claim", table: "document_change_analysis");
        migrationBuilder.DropIndex(name: "ix_statement_analysis_active_claim", table: "statement_analysis");
        migrationBuilder.DropIndex(name: "ix_action_analysis_active_claim", table: "action_analysis");
    }
}
