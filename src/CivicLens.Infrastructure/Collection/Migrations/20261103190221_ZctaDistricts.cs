using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations
{
    /// <inheritdoc />
    public partial class ZctaDistricts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "zcta_districts",
                columns: table => new
                {
                    attempt_id = table.Column<string>(type: "text", nullable: false),
                    zcta = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    state = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    district = table.Column<int>(type: "integer", nullable: false),
                    land_area_square_meters = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_zcta_districts", x => new { x.attempt_id, x.zcta, x.state, x.district });
                    table.CheckConstraint("ck_zcta_districts_land", "district >= 0 AND land_area_square_meters > 0");
                    table.CheckConstraint("ck_zcta_districts_zcta", "zcta ~ '^[0-9]{5}$'");
                    table.ForeignKey(
                        name: "FK_zcta_districts_roster_snapshots_attempt_id",
                        column: x => x.attempt_id,
                        principalTable: "roster_snapshots",
                        principalColumn: "attempt_id",
                        onDelete: ReferentialAction.Restrict);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "zcta_districts");
        }
    }
}
