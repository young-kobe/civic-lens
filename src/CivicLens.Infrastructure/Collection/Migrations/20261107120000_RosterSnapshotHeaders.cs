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
                attempt_id = table.Column<string>(type: "text", nullable: false)
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
            INSERT INTO roster_snapshots (attempt_id)
            SELECT attempts.attempt_id
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

        migrationBuilder.CreateTable(
            name: "executive_officials",
            columns: table => new
            {
                attempt_id = table.Column<string>(type: "text", nullable: false),
                govtrack_id = table.Column<int>(type: "integer", nullable: false),
                wikidata_id = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                full_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_executive_officials", x => new { x.attempt_id, x.govtrack_id });
                table.CheckConstraint("ck_executive_officials_identity", "govtrack_id > 0 AND (wikidata_id IS NULL OR wikidata_id ~ '^Q[1-9][0-9]*$') AND length(btrim(full_name)) > 0");
                table.ForeignKey(
                    name: "FK_executive_officials_roster_snapshots_attempt_id",
                    column: x => x.attempt_id,
                    principalTable: "roster_snapshots",
                    principalColumn: "attempt_id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "executive_terms",
            columns: table => new
            {
                attempt_id = table.Column<string>(type: "text", nullable: false),
                govtrack_id = table.Column<int>(type: "integer", nullable: false),
                term_index = table.Column<int>(type: "integer", nullable: false),
                office = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                starts_on = table.Column<DateOnly>(type: "date", nullable: false),
                ends_on = table.Column<DateOnly>(type: "date", nullable: false),
                party = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_executive_terms", x => new { x.attempt_id, x.govtrack_id, x.term_index });
                table.CheckConstraint("ck_executive_terms_dates", "term_index >= 0 AND ends_on >= starts_on");
                table.CheckConstraint("ck_executive_terms_office", "office IN ('prez', 'viceprez')");
                table.ForeignKey(
                    name: "FK_executive_terms_executive_officials_attempt_id_govtrack_id",
                    columns: x => new { x.attempt_id, x.govtrack_id },
                    principalTable: "executive_officials",
                    principalColumns: new[] { "attempt_id", "govtrack_id" },
                    onDelete: ReferentialAction.Restrict);
            });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "executive_terms");

        migrationBuilder.DropTable(
            name: "executive_officials");

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
