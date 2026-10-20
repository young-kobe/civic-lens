using System;
using CivicLens.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations;

[DbContext(typeof(CollectionAttemptDbContext))]
[Migration("20261106120000_RosterSnapshots")]
public partial class RosterSnapshots : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "roster_members",
            columns: table => new
            {
                attempt_id = table.Column<string>(type: "text", nullable: false),
                bioguide_id = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                lis_id = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                full_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                office = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                phone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                url = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_roster_members", x => new { x.attempt_id, x.bioguide_id });
                table.CheckConstraint("ck_roster_members_identity", "bioguide_id ~ '^[A-Z][0-9]{6}$' AND length(btrim(full_name)) > 0");
                table.ForeignKey(
                    name: "FK_roster_members_collection_attempts_attempt_id",
                    column: x => x.attempt_id,
                    principalTable: "collection_attempts",
                    principalColumn: "attempt_id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "roster_terms",
            columns: table => new
            {
                attempt_id = table.Column<string>(type: "text", nullable: false),
                bioguide_id = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                term_index = table.Column<int>(type: "integer", nullable: false),
                chamber = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                starts_on = table.Column<DateOnly>(type: "date", nullable: false),
                ends_on = table.Column<DateOnly>(type: "date", nullable: false),
                state = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                district = table.Column<int>(type: "integer", nullable: true),
                senate_class = table.Column<int>(type: "integer", nullable: true),
                party = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_roster_terms", x => new { x.attempt_id, x.bioguide_id, x.term_index });
                table.CheckConstraint("ck_roster_terms_chamber", "chamber IN ('sen', 'rep')");
                table.CheckConstraint("ck_roster_terms_dates", "term_index >= 0 AND ends_on >= starts_on");
                table.ForeignKey(
                    name: "FK_roster_terms_roster_members_attempt_id_bioguide_id",
                    columns: x => new { x.attempt_id, x.bioguide_id },
                    principalTable: "roster_members",
                    principalColumns: new[] { "attempt_id", "bioguide_id" },
                    onDelete: ReferentialAction.Restrict);
            });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "roster_terms");

        migrationBuilder.DropTable(
            name: "roster_members");
    }
}
