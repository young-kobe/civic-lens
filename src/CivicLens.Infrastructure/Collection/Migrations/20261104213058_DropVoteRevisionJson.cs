using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations
{
    /// <inheritdoc />
    public partial class DropVoteRevisionJson : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("LOCK TABLE vote_revisions IN SHARE ROW EXCLUSIVE MODE");
            migrationBuilder.Sql("""
                DO $check$
                DECLARE legacy text;
                BEGIN
                    SELECT record_id || '/' || revision_number INTO legacy FROM vote_revisions
                    WHERE headline IS NULL ORDER BY record_id, revision_number LIMIT 1;
                    IF legacy IS NOT NULL THEN
                        RAISE EXCEPTION 'Cannot drop revision_json: vote revision % has no columns', legacy;
                    END IF;
                END
                $check$
                """);
            migrationBuilder.DropColumn(
                name: "revision_json",
                table: "vote_revisions");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "revision_json",
                table: "vote_revisions",
                type: "text",
                nullable: true);
        }
    }
}
