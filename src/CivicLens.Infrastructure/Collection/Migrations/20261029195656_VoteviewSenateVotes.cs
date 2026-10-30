using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations
{
    /// <inheritdoc />
    public partial class VoteviewSenateVotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_vote_candidates_vote_versions_detail_job_id",
                table: "vote_candidates");

            migrationBuilder.DropPrimaryKey(
                name: "PK_vote_versions",
                table: "vote_versions");

            migrationBuilder.DropIndex(
                name: "ux_vote_candidates_detail_job",
                table: "vote_candidates");

            migrationBuilder.AddColumn<string>(
                name: "roster_job_id",
                table: "vote_candidates",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "part",
                table: "document_extractions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_vote_versions",
                table: "vote_versions",
                columns: new[] { "detail_job_id", "session_number", "roll_call_number" });

            migrationBuilder.CreateIndex(
                name: "IX_vote_candidates_roster_job_id",
                table: "vote_candidates",
                column: "roster_job_id");

            migrationBuilder.CreateIndex(
                name: "ux_vote_candidates_detail_job",
                table: "vote_candidates",
                columns: new[] { "detail_job_id", "session_number", "roll_call_number" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_document_extractions_part",
                table: "document_extractions",
                sql: "part IS NULL OR part >= 0");

            migrationBuilder.AddForeignKey(
                name: "FK_vote_candidates_collection_jobs_roster_job_id",
                table: "vote_candidates",
                column: "roster_job_id",
                principalTable: "collection_jobs",
                principalColumn: "job_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_vote_candidates_vote_versions_detail_job_id_session_number_~",
                table: "vote_candidates",
                columns: new[] { "detail_job_id", "session_number", "roll_call_number" },
                principalTable: "vote_versions",
                principalColumns: new[] { "detail_job_id", "session_number", "roll_call_number" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_vote_candidates_collection_jobs_roster_job_id",
                table: "vote_candidates");

            migrationBuilder.DropForeignKey(
                name: "FK_vote_candidates_vote_versions_detail_job_id_session_number_~",
                table: "vote_candidates");

            migrationBuilder.DropPrimaryKey(
                name: "PK_vote_versions",
                table: "vote_versions");

            migrationBuilder.DropIndex(
                name: "IX_vote_candidates_roster_job_id",
                table: "vote_candidates");

            migrationBuilder.DropIndex(
                name: "ux_vote_candidates_detail_job",
                table: "vote_candidates");

            migrationBuilder.DropCheckConstraint(
                name: "ck_document_extractions_part",
                table: "document_extractions");

            migrationBuilder.DropColumn(
                name: "roster_job_id",
                table: "vote_candidates");

            migrationBuilder.DropColumn(
                name: "part",
                table: "document_extractions");

            migrationBuilder.AddPrimaryKey(
                name: "PK_vote_versions",
                table: "vote_versions",
                column: "detail_job_id");

            migrationBuilder.CreateIndex(
                name: "ux_vote_candidates_detail_job",
                table: "vote_candidates",
                column: "detail_job_id",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_vote_candidates_vote_versions_detail_job_id",
                table: "vote_candidates",
                column: "detail_job_id",
                principalTable: "vote_versions",
                principalColumn: "detail_job_id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
