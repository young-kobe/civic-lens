using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations;

public partial class ReleaseBillCounts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "release_bill_counts",
            columns: table => new
            {
                release_number = table.Column<int>(type: "integer", nullable: false),
                congress = table.Column<int>(type: "integer", nullable: false),
                bills = table.Column<int>(type: "integer", nullable: false),
                expected_member_row_count = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_release_bill_counts", x => new { x.release_number, x.congress });
                table.CheckConstraint("ck_release_bill_counts_congress", "congress > 0");
                table.CheckConstraint("ck_release_bill_counts_bills", "bills >= 0");
                table.CheckConstraint("ck_release_bill_counts_expected_member_row_count", "expected_member_row_count >= 0");
                table.ForeignKey("FK_release_bill_counts_publication_releases_release_number", x => x.release_number,
                    principalTable: "publication_releases", principalColumn: "release_number", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "release_official_bill_counts",
            columns: table => new
            {
                release_number = table.Column<int>(type: "integer", nullable: false),
                congress = table.Column<int>(type: "integer", nullable: false),
                official_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                sponsored = table.Column<int>(type: "integer", nullable: false),
                cosponsored = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_release_official_bill_counts", x => new { x.release_number, x.congress, x.official_id });
                table.CheckConstraint("ck_release_official_bill_counts_congress", "congress > 0");
                table.CheckConstraint("ck_release_official_bill_counts_official_id", "length(btrim(official_id)) > 0");
                table.CheckConstraint("ck_release_official_bill_counts_sponsored", "sponsored >= 0");
                table.CheckConstraint("ck_release_official_bill_counts_cosponsored", "cosponsored >= 0");
                table.CheckConstraint("ck_release_official_bill_counts_nonzero", "sponsored + cosponsored > 0");
                table.ForeignKey("fk_release_official_bill_counts_header",
                    x => new { x.release_number, x.congress }, "release_bill_counts", new[] { "release_number", "congress" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey("fk_release_official_bill_counts_official",
                    x => new { x.release_number, x.official_id }, "release_officials", new[] { "release_number", "official_id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "ix_release_official_bill_counts_release_official",
            table: "release_official_bill_counts",
            columns: new[] { "release_number", "official_id" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "release_official_bill_counts");
        migrationBuilder.DropTable(name: "release_bill_counts");
    }
}
