using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations
{
    /// <inheritdoc />
    public partial class VoteRevisionRecentIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_vote_revisions_recent",
                table: "vote_revisions",
                columns: new[] { "voted_on", "chamber", "session_number", "roll_call_number", "record_id" },
                descending: new[] { true, false, true, true, false },
                filter: "member_list_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_vote_revisions_recent",
                table: "vote_revisions");
        }
    }
}
