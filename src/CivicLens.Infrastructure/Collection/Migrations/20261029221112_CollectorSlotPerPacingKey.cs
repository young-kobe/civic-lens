using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations
{
    /// <inheritdoc />
    public partial class CollectorSlotPerPacingKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM collection_collector_slot");

            migrationBuilder.DropPrimaryKey(
                name: "PK_collection_collector_slot",
                table: "collection_collector_slot");

            migrationBuilder.DropCheckConstraint(
                name: "ck_collector_singleton",
                table: "collection_collector_slot");

            migrationBuilder.DropColumn(
                name: "id",
                table: "collection_collector_slot");

            migrationBuilder.AddColumn<string>(
                name: "pacing_key",
                table: "collection_collector_slot",
                type: "text",
                nullable: false);

            migrationBuilder.AddPrimaryKey(
                name: "PK_collection_collector_slot",
                table: "collection_collector_slot",
                column: "pacing_key");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM collection_collector_slot");
            migrationBuilder.Sql("UPDATE collection_job_origins SET unresolved_attempt_id = NULL WHERE origin LIKE 'group:%'");

            migrationBuilder.DropPrimaryKey(
                name: "PK_collection_collector_slot",
                table: "collection_collector_slot");

            migrationBuilder.DropColumn(
                name: "pacing_key",
                table: "collection_collector_slot");

            migrationBuilder.AddColumn<int>(
                name: "id",
                table: "collection_collector_slot",
                type: "integer",
                nullable: false);

            migrationBuilder.AddPrimaryKey(
                name: "PK_collection_collector_slot",
                table: "collection_collector_slot",
                column: "id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_collector_singleton",
                table: "collection_collector_slot",
                sql: "id = 1");
        }
    }
}
