using CivicLens.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations;

[DbContext(typeof(CollectionAttemptDbContext))]
[Migration("20261103120000_AnalysisPriority")]
public partial class AnalysisPriority : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<short>(name: "priority", table: "statement_analysis", type: "smallint", nullable: false, defaultValue: (short)3);
        migrationBuilder.AddCheckConstraint(name: "ck_statement_analysis_priority", table: "statement_analysis", sql: "priority BETWEEN 1 AND 3");
        migrationBuilder.DropIndex(name: "ix_statement_analysis_active_claim", table: "statement_analysis");
        migrationBuilder.CreateIndex(name: "ix_statement_analysis_active_claim", table: "statement_analysis", columns: new[] { "task", "priority", "created_at_utc_ticks", "candidate_id" },
            filter: "status IN ('Pending','RetryWaiting','WaitingForBudget','Running')");
        migrationBuilder.AddColumn<short>(name: "priority", table: "action_analysis", type: "smallint", nullable: false, defaultValue: (short)3);
        migrationBuilder.AddCheckConstraint(name: "ck_action_analysis_priority", table: "action_analysis", sql: "priority BETWEEN 1 AND 3");
        migrationBuilder.DropIndex(name: "ix_action_analysis_active_claim", table: "action_analysis");
        migrationBuilder.CreateIndex(name: "ix_action_analysis_active_claim", table: "action_analysis", columns: new[] { "task", "priority", "created_at_utc_ticks", "candidate_id" },
            filter: "status IN ('Pending','RetryWaiting','WaitingForBudget','Running')");
        migrationBuilder.AddColumn<short>(name: "priority", table: "vote_analysis", type: "smallint", nullable: false, defaultValue: (short)3);
        migrationBuilder.AddCheckConstraint(name: "ck_vote_analysis_priority", table: "vote_analysis", sql: "priority BETWEEN 1 AND 3");
        migrationBuilder.DropIndex(name: "ix_vote_analysis_active_claim", table: "vote_analysis");
        migrationBuilder.CreateIndex(name: "ix_vote_analysis_active_claim", table: "vote_analysis", columns: new[] { "task", "priority", "created_at_utc_ticks", "candidate_id" },
            filter: "status IN ('Pending','RetryWaiting','WaitingForBudget','Running')");
        migrationBuilder.AddColumn<short>(name: "priority", table: "document_change_analysis", type: "smallint", nullable: false, defaultValue: (short)3);
        migrationBuilder.AddCheckConstraint(name: "ck_document_change_analysis_priority", table: "document_change_analysis", sql: "priority BETWEEN 1 AND 3");
        migrationBuilder.DropIndex(name: "ix_document_change_analysis_active_claim", table: "document_change_analysis");
        migrationBuilder.CreateIndex(name: "ix_document_change_analysis_active_claim", table: "document_change_analysis", columns: new[] { "task", "priority", "created_at_utc_ticks", "comparison_id" },
            filter: "status IN ('Pending','RetryWaiting','WaitingForBudget','Running')");
        migrationBuilder.AddColumn<short>(name: "priority", table: "brief_analysis", type: "smallint", nullable: false, defaultValue: (short)3);
        migrationBuilder.AddCheckConstraint(name: "ck_brief_analysis_priority", table: "brief_analysis", sql: "priority BETWEEN 1 AND 3");
        migrationBuilder.DropIndex(name: "ix_brief_analysis_active_claim", table: "brief_analysis");
        migrationBuilder.CreateIndex(name: "ix_brief_analysis_active_claim", table: "brief_analysis", columns: new[] { "task", "priority", "created_at_utc_ticks", "record_id" },
            filter: "status IN ('Pending','RetryWaiting','WaitingForBudget','Running')");
        migrationBuilder.AddColumn<short>(name: "priority", table: "relationship_analysis", type: "smallint", nullable: false, defaultValue: (short)3);
        migrationBuilder.AddCheckConstraint(name: "ck_relationship_analysis_priority", table: "relationship_analysis", sql: "priority BETWEEN 1 AND 3");
        migrationBuilder.DropIndex(name: "ix_relationship_analysis_active_claim", table: "relationship_analysis");
        migrationBuilder.CreateIndex(name: "ix_relationship_analysis_active_claim", table: "relationship_analysis", columns: new[] { "task", "priority", "created_at_utc_ticks", "pair_id" },
            filter: "status IN ('Pending','RetryWaiting','WaitingForBudget','Running')");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "ix_statement_analysis_active_claim", table: "statement_analysis");
        migrationBuilder.CreateIndex(name: "ix_statement_analysis_active_claim", table: "statement_analysis", columns: new[] { "task", "created_at_utc_ticks", "candidate_id" },
            filter: "status IN ('Pending','RetryWaiting','WaitingForBudget','Running')");
        migrationBuilder.DropCheckConstraint(name: "ck_statement_analysis_priority", table: "statement_analysis");
        migrationBuilder.DropColumn(name: "priority", table: "statement_analysis");
        migrationBuilder.DropIndex(name: "ix_action_analysis_active_claim", table: "action_analysis");
        migrationBuilder.CreateIndex(name: "ix_action_analysis_active_claim", table: "action_analysis", columns: new[] { "task", "created_at_utc_ticks", "candidate_id" },
            filter: "status IN ('Pending','RetryWaiting','WaitingForBudget','Running')");
        migrationBuilder.DropCheckConstraint(name: "ck_action_analysis_priority", table: "action_analysis");
        migrationBuilder.DropColumn(name: "priority", table: "action_analysis");
        migrationBuilder.DropIndex(name: "ix_vote_analysis_active_claim", table: "vote_analysis");
        migrationBuilder.CreateIndex(name: "ix_vote_analysis_active_claim", table: "vote_analysis", columns: new[] { "task", "created_at_utc_ticks", "candidate_id" },
            filter: "status IN ('Pending','RetryWaiting','WaitingForBudget','Running')");
        migrationBuilder.DropCheckConstraint(name: "ck_vote_analysis_priority", table: "vote_analysis");
        migrationBuilder.DropColumn(name: "priority", table: "vote_analysis");
        migrationBuilder.DropIndex(name: "ix_document_change_analysis_active_claim", table: "document_change_analysis");
        migrationBuilder.CreateIndex(name: "ix_document_change_analysis_active_claim", table: "document_change_analysis", columns: new[] { "task", "created_at_utc_ticks", "comparison_id" },
            filter: "status IN ('Pending','RetryWaiting','WaitingForBudget','Running')");
        migrationBuilder.DropCheckConstraint(name: "ck_document_change_analysis_priority", table: "document_change_analysis");
        migrationBuilder.DropColumn(name: "priority", table: "document_change_analysis");
        migrationBuilder.DropIndex(name: "ix_brief_analysis_active_claim", table: "brief_analysis");
        migrationBuilder.CreateIndex(name: "ix_brief_analysis_active_claim", table: "brief_analysis", columns: new[] { "task", "created_at_utc_ticks", "record_id" },
            filter: "status IN ('Pending','RetryWaiting','WaitingForBudget','Running')");
        migrationBuilder.DropCheckConstraint(name: "ck_brief_analysis_priority", table: "brief_analysis");
        migrationBuilder.DropColumn(name: "priority", table: "brief_analysis");
        migrationBuilder.DropIndex(name: "ix_relationship_analysis_active_claim", table: "relationship_analysis");
        migrationBuilder.CreateIndex(name: "ix_relationship_analysis_active_claim", table: "relationship_analysis", columns: new[] { "task", "created_at_utc_ticks", "pair_id" },
            filter: "status IN ('Pending','RetryWaiting','WaitingForBudget','Running')");
        migrationBuilder.DropCheckConstraint(name: "ck_relationship_analysis_priority", table: "relationship_analysis");
        migrationBuilder.DropColumn(name: "priority", table: "relationship_analysis");
    }
}
