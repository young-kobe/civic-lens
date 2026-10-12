using CivicLens.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations;

[DbContext(typeof(CollectionAttemptDbContext))]
[Migration("20261012200000_StatementDrafting")]
public partial class StatementDrafting : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(name: "ck_statement_analysis_runs_revision", table: "statement_analysis_runs");
        migrationBuilder.AddCheckConstraint(name: "ck_statement_analysis_runs_revision", table: "statement_analysis_runs",
            sql: "issue_revision_id IS NOT NULL OR outcome = 'Interrupted' OR task <> 'gate'");

        migrationBuilder.CreateTable(
            name: "statement_drafts",
            columns: table => new
            {
                candidate_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                current_revision_number = table.Column<int>(type: "integer", nullable: false),
                created_at_utc_ticks = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_statement_drafts", x => x.candidate_id);
                table.CheckConstraint("ck_statement_drafts_revision", "current_revision_number >= 0");
                table.ForeignKey(
                    name: "FK_statement_drafts_statement_candidates_candidate_id",
                    column: x => x.candidate_id,
                    principalTable: "statement_candidates",
                    principalColumn: "candidate_id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "statement_revisions",
            columns: table => new
            {
                candidate_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                revision_number = table.Column<int>(type: "integer", nullable: false),
                author_subject = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                author_kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                run_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                revision_json = table.Column<string>(type: "text", nullable: false),
                created_at_utc_ticks = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_statement_revisions", x => new { x.candidate_id, x.revision_number });
                table.CheckConstraint("ck_statement_revisions_number", "revision_number >= 1");
                table.CheckConstraint("ck_statement_revisions_author_kind", "author_kind IN ('Person','Analysis')");
                table.CheckConstraint("ck_statement_revisions_run", "(author_kind = 'Analysis') = (run_id IS NOT NULL)");
                table.ForeignKey(
                    name: "FK_statement_revisions_statement_drafts_candidate_id",
                    column: x => x.candidate_id,
                    principalTable: "statement_drafts",
                    principalColumn: "candidate_id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_statement_revisions_statement_analysis_runs_run_id",
                    column: x => x.run_id,
                    principalTable: "statement_analysis_runs",
                    principalColumn: "run_id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_statement_revisions_run_id",
            table: "statement_revisions",
            column: "run_id",
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "statement_revisions");
        migrationBuilder.DropTable(name: "statement_drafts");
        migrationBuilder.DropCheckConstraint(name: "ck_statement_analysis_runs_revision", table: "statement_analysis_runs");
        migrationBuilder.AddCheckConstraint(name: "ck_statement_analysis_runs_revision", table: "statement_analysis_runs",
            sql: "issue_revision_id IS NOT NULL OR outcome = 'Interrupted'");
    }
}
