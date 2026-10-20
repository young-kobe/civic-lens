using CivicLens.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations;

[DbContext(typeof(CollectionAttemptDbContext))]
[Migration("20261107120000_RosterSnapshotHeaders")]
public partial class RosterSnapshotHeaders : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "roster_snapshots",
            columns: table => new
            {
                attempt_id = table.Column<string>(type: "text", nullable: false),
                source_id = table.Column<string>(type: "text", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_roster_snapshots", x => x.attempt_id);
                table.ForeignKey(
                    name: "FK_roster_snapshots_collection_attempts_attempt_id",
                    column: x => x.attempt_id,
                    principalTable: "collection_attempts",
                    principalColumn: "attempt_id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.Sql("""
            INSERT INTO roster_snapshots (attempt_id, source_id)
            SELECT attempts.attempt_id, attempts.source_id
            FROM collection_attempts AS attempts
            WHERE EXISTS (SELECT 1 FROM roster_members AS members WHERE members.attempt_id = attempts.attempt_id)
            """);

        migrationBuilder.DropForeignKey(
            name: "FK_roster_members_collection_attempts_attempt_id",
            table: "roster_members");

        migrationBuilder.AddForeignKey(
            name: "FK_roster_members_roster_snapshots_attempt_id",
            table: "roster_members",
            column: "attempt_id",
            principalTable: "roster_snapshots",
            principalColumn: "attempt_id",
            onDelete: ReferentialAction.Restrict);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_roster_members_roster_snapshots_attempt_id",
            table: "roster_members");

        migrationBuilder.AddForeignKey(
            name: "FK_roster_members_collection_attempts_attempt_id",
            table: "roster_members",
            column: "attempt_id",
            principalTable: "collection_attempts",
            principalColumn: "attempt_id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.DropTable(
            name: "roster_snapshots");
    }
}
