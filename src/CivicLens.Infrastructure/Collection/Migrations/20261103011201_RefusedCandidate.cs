using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations
{
    /// <inheritdoc />
    public partial class RefusedCandidate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "refused_candidate_id",
                table: "vote_drafts",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "refused_candidate_id",
                table: "action_drafts",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_vote_drafts_refused_candidate_id",
                table: "vote_drafts",
                column: "refused_candidate_id");

            migrationBuilder.CreateIndex(
                name: "IX_action_drafts_refused_candidate_id",
                table: "action_drafts",
                column: "refused_candidate_id");

            migrationBuilder.AddForeignKey(
                name: "FK_action_drafts_action_candidates_refused_candidate_id",
                table: "action_drafts",
                column: "refused_candidate_id",
                principalTable: "action_candidates",
                principalColumn: "candidate_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_vote_drafts_vote_candidates_refused_candidate_id",
                table: "vote_drafts",
                column: "refused_candidate_id",
                principalTable: "vote_candidates",
                principalColumn: "candidate_id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_action_drafts_action_candidates_refused_candidate_id",
                table: "action_drafts");

            migrationBuilder.DropForeignKey(
                name: "FK_vote_drafts_vote_candidates_refused_candidate_id",
                table: "vote_drafts");

            migrationBuilder.DropIndex(
                name: "IX_vote_drafts_refused_candidate_id",
                table: "vote_drafts");

            migrationBuilder.DropIndex(
                name: "IX_action_drafts_refused_candidate_id",
                table: "action_drafts");

            migrationBuilder.DropColumn(
                name: "refused_candidate_id",
                table: "vote_drafts");

            migrationBuilder.DropColumn(
                name: "refused_candidate_id",
                table: "action_drafts");
        }
    }
}
