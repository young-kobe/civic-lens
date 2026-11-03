using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations
{
    /// <inheritdoc />
    public partial class AttemptElapsed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "elapsed_milliseconds",
                table: "collection_attempts",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_collection_attempts_elapsed_milliseconds",
                table: "collection_attempts",
                sql: "elapsed_milliseconds IS NULL OR elapsed_milliseconds BETWEEN 0 AND 305000");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_collection_attempts_elapsed_milliseconds",
                table: "collection_attempts");

            migrationBuilder.DropColumn(
                name: "elapsed_milliseconds",
                table: "collection_attempts");
        }
    }
}
