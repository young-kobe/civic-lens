using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations
{
    /// <inheritdoc />
    public partial class ReviewRecordLastRevised : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "last_revised_at_utc_ticks",
                table: "vote_drafts",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "last_revised_at_utc_ticks",
                table: "action_drafts",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            foreach (var kind in new[] { "action", "vote" })
                migrationBuilder.Sql($"UPDATE {kind}_drafts d SET last_revised_at_utc_ticks = COALESCE(" +
                    $"(SELECT max(r.created_at_utc_ticks) FROM {kind}_revisions r WHERE r.record_id = d.record_id), 0)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "last_revised_at_utc_ticks",
                table: "vote_drafts");

            migrationBuilder.DropColumn(
                name: "last_revised_at_utc_ticks",
                table: "action_drafts");
        }
    }
}
