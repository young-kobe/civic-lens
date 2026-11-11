using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations;

public partial class ReleaseIssuePartyLineups : Migration
{
    private const string ViewSql = """
        CREATE VIEW public_api.release_issue_party_lineups WITH (security_barrier = true) AS
        SELECT l.release_number, l.issue_id, l.unknown_bills, c.party, c.bills
          FROM public.release_issue_party_lineups l
          JOIN public.publication_releases r ON r.release_number = l.release_number
          LEFT JOIN public.release_issue_party_counts c ON c.release_number = l.release_number AND c.issue_id = l.issue_id
         WHERE r.is_active;

        ALTER VIEW public_api.release_issue_party_lineups OWNER TO civic_lens_public_owner;
        """;

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "release_issue_party_lineups",
            columns: table => new
            {
                release_number = table.Column<int>(type: "integer", nullable: false),
                issue_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                unknown_bills = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_release_issue_party_lineups", x => new { x.release_number, x.issue_id });
                table.CheckConstraint("ck_release_issue_party_lineups_unknown_bills", "unknown_bills >= 0");
                table.ForeignKey(
                    name: "fk_release_issue_party_lineups_issue",
                    columns: x => new { x.release_number, x.issue_id },
                    principalTable: "release_issues",
                    principalColumns: new[] { "release_number", "issue_id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "release_issue_party_counts",
            columns: table => new
            {
                release_number = table.Column<int>(type: "integer", nullable: false),
                issue_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                party = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                bills = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_release_issue_party_counts", x => new { x.release_number, x.issue_id, x.party });
                table.CheckConstraint("ck_release_issue_party_counts_bills", "bills > 0");
                table.CheckConstraint("ck_release_issue_party_counts_party", "length(btrim(party)) > 0");
                table.ForeignKey(
                    name: "fk_release_issue_party_counts_lineup",
                    columns: x => new { x.release_number, x.issue_id },
                    principalTable: "release_issue_party_lineups",
                    principalColumns: new[] { "release_number", "issue_id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.Sql(ViewSql);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP VIEW public_api.release_issue_party_lineups;");
        migrationBuilder.DropTable(name: "release_issue_party_counts");
        migrationBuilder.DropTable(name: "release_issue_party_lineups");
    }
}
