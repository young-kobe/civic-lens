using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations
{
    /// <inheritdoc />
    public partial class ReviewHeldReason : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "held_reason",
                table: "vote_drafts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "held_reason",
                table: "statement_drafts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "last_revised_at_utc_ticks",
                table: "statement_drafts",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "held_reason",
                table: "executive_action_drafts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "last_revised_at_utc_ticks",
                table: "document_change_drafts",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "held_reason",
                table: "action_drafts",
                type: "text",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE statement_drafts d SET last_revised_at_utc_ticks = COALESCE(
                    (SELECT max(r.created_at_utc_ticks) FROM statement_revisions r WHERE r.candidate_id = d.candidate_id), d.created_at_utc_ticks)
                """);

            migrationBuilder.Sql("""
                UPDATE document_change_drafts d SET last_revised_at_utc_ticks = COALESCE(
                    (SELECT max(r.created_at_utc_ticks) FROM document_change_revisions r WHERE r.draft_id = d.draft_id), d.created_at_utc_ticks)
                """);

            migrationBuilder.CreateIndex(
                name: "ix_vote_drafts_needs_action_revised",
                table: "vote_drafts",
                columns: new[] { "last_revised_at_utc_ticks", "record_id" },
                descending: new bool[0],
                filter: "review_status <> 'Approved'");

            migrationBuilder.CreateIndex(
                name: "ix_statement_drafts_needs_action_revised",
                table: "statement_drafts",
                columns: new[] { "last_revised_at_utc_ticks", "candidate_id" },
                descending: new bool[0],
                filter: "review_status <> 'Approved'");

            migrationBuilder.CreateIndex(
                name: "ix_relationship_drafts_needs_action_revised",
                table: "relationship_drafts",
                columns: new[] { "last_revised_at_utc_ticks", "record_id" },
                descending: new bool[0],
                filter: "review_status <> 'Approved'");

            migrationBuilder.CreateIndex(
                name: "ix_executive_action_drafts_needs_action_revised",
                table: "executive_action_drafts",
                columns: new[] { "last_revised_at_utc_ticks", "record_id" },
                descending: new bool[0],
                filter: "review_status <> 'Approved'");

            migrationBuilder.CreateIndex(
                name: "ix_document_change_drafts_needs_action_revised",
                table: "document_change_drafts",
                columns: new[] { "last_revised_at_utc_ticks", "draft_id" },
                descending: new bool[0],
                filter: "review_status <> 'Approved'");

            migrationBuilder.CreateIndex(
                name: "ix_brief_drafts_needs_action_revised",
                table: "brief_drafts",
                columns: new[] { "last_revised_at_utc_ticks", "record_id" },
                descending: new bool[0],
                filter: "review_status <> 'Approved'");

            migrationBuilder.CreateIndex(
                name: "ix_action_drafts_needs_action_revised",
                table: "action_drafts",
                columns: new[] { "last_revised_at_utc_ticks", "record_id" },
                descending: new bool[0],
                filter: "review_status <> 'Approved'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_vote_drafts_needs_action_revised",
                table: "vote_drafts");

            migrationBuilder.DropIndex(
                name: "ix_statement_drafts_needs_action_revised",
                table: "statement_drafts");

            migrationBuilder.DropIndex(
                name: "ix_relationship_drafts_needs_action_revised",
                table: "relationship_drafts");

            migrationBuilder.DropIndex(
                name: "ix_executive_action_drafts_needs_action_revised",
                table: "executive_action_drafts");

            migrationBuilder.DropIndex(
                name: "ix_document_change_drafts_needs_action_revised",
                table: "document_change_drafts");

            migrationBuilder.DropIndex(
                name: "ix_brief_drafts_needs_action_revised",
                table: "brief_drafts");

            migrationBuilder.DropIndex(
                name: "ix_action_drafts_needs_action_revised",
                table: "action_drafts");

            migrationBuilder.DropColumn(
                name: "held_reason",
                table: "vote_drafts");

            migrationBuilder.DropColumn(
                name: "held_reason",
                table: "statement_drafts");

            migrationBuilder.DropColumn(
                name: "last_revised_at_utc_ticks",
                table: "statement_drafts");

            migrationBuilder.DropColumn(
                name: "held_reason",
                table: "executive_action_drafts");

            migrationBuilder.DropColumn(
                name: "last_revised_at_utc_ticks",
                table: "document_change_drafts");

            migrationBuilder.DropColumn(
                name: "held_reason",
                table: "action_drafts");
        }
    }
}
