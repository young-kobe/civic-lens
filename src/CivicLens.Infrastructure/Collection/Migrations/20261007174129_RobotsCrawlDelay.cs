using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations
{
    /// <inheritdoc />
    public partial class RobotsCrawlDelay : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "robots_crawl_delay_milliseconds",
                table: "collection_attempts",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_collection_attempts_robots_delay",
                table: "collection_attempts",
                sql: "robots_crawl_delay_milliseconds IS NULL OR robots_crawl_delay_milliseconds BETWEEN 0 AND 922337203685000");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_collection_attempts_robots_delay",
                table: "collection_attempts");

            migrationBuilder.DropColumn(
                name: "robots_crawl_delay_milliseconds",
                table: "collection_attempts");
        }
    }
}
