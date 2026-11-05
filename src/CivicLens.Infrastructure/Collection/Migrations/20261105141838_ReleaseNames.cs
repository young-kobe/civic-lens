using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations
{
    /// <inheritdoc />
    public partial class ReleaseNames : Migration
    {
        // Each view shows the rows of the active release only, as active_records does.
        private const string UpSql = """
            CREATE VIEW public_api.release_officials WITH (security_barrier = true) AS
            SELECT o.release_number, o.official_id, o.name, o.wikidata_id, o.website
              FROM public.release_officials o JOIN public.publication_releases r ON r.release_number = o.release_number
             WHERE r.is_active;

            CREATE VIEW public_api.release_official_terms WITH (security_barrier = true) AS
            SELECT t.release_number, t.official_id, t."position", t.office, t.kind, t.state, t.district, t.party, t.caucus, t.start_on, t.end_on
              FROM public.release_official_terms t JOIN public.publication_releases r ON r.release_number = t.release_number
             WHERE r.is_active;

            CREATE VIEW public_api.release_issues WITH (security_barrier = true) AS
            SELECT i.release_number, i.issue_id, i.name, i.definition
              FROM public.release_issues i JOIN public.publication_releases r ON r.release_number = i.release_number
             WHERE r.is_active;

            CREATE VIEW public_api.release_issue_includes WITH (security_barrier = true) AS
            SELECT i.release_number, i.issue_id, i."position", i.text
              FROM public.release_issue_includes i JOIN public.publication_releases r ON r.release_number = i.release_number
             WHERE r.is_active;

            CREATE VIEW public_api.release_issue_excludes WITH (security_barrier = true) AS
            SELECT i.release_number, i.issue_id, i."position", i.text
              FROM public.release_issue_excludes i JOIN public.publication_releases r ON r.release_number = i.release_number
             WHERE r.is_active;

            CREATE VIEW public_api.release_policy_areas WITH (security_barrier = true) AS
            SELECT a.release_number, a.policy_area_id, a.name
              FROM public.release_policy_areas a JOIN public.publication_releases r ON r.release_number = a.release_number
             WHERE r.is_active;

            ALTER VIEW public_api.release_officials OWNER TO civic_lens_public_owner;
            ALTER VIEW public_api.release_official_terms OWNER TO civic_lens_public_owner;
            ALTER VIEW public_api.release_issues OWNER TO civic_lens_public_owner;
            ALTER VIEW public_api.release_issue_includes OWNER TO civic_lens_public_owner;
            ALTER VIEW public_api.release_issue_excludes OWNER TO civic_lens_public_owner;
            ALTER VIEW public_api.release_policy_areas OWNER TO civic_lens_public_owner;
            """;

        private const string DownSql = """
            DROP VIEW public_api.release_officials, public_api.release_official_terms, public_api.release_issues,
                public_api.release_issue_includes, public_api.release_issue_excludes, public_api.release_policy_areas;
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "release_issues",
                columns: table => new
                {
                    release_number = table.Column<int>(type: "integer", nullable: false),
                    issue_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    definition = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_release_issues", x => new { x.release_number, x.issue_id });
                    table.CheckConstraint("ck_release_issues_issue_id", "length(btrim(issue_id)) > 0");
                    table.CheckConstraint("ck_release_issues_name", "length(btrim(name)) > 0");
                    table.ForeignKey(
                        name: "FK_release_issues_publication_releases_release_number",
                        column: x => x.release_number,
                        principalTable: "publication_releases",
                        principalColumn: "release_number",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "release_officials",
                columns: table => new
                {
                    release_number = table.Column<int>(type: "integer", nullable: false),
                    official_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    wikidata_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    website = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_release_officials", x => new { x.release_number, x.official_id });
                    table.CheckConstraint("ck_release_officials_name", "length(btrim(name)) > 0");
                    table.CheckConstraint("ck_release_officials_official_id", "length(btrim(official_id)) > 0");
                    table.ForeignKey(
                        name: "FK_release_officials_publication_releases_release_number",
                        column: x => x.release_number,
                        principalTable: "publication_releases",
                        principalColumn: "release_number",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "release_policy_areas",
                columns: table => new
                {
                    release_number = table.Column<int>(type: "integer", nullable: false),
                    policy_area_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_release_policy_areas", x => new { x.release_number, x.policy_area_id });
                    table.CheckConstraint("ck_release_policy_areas_name", "length(btrim(name)) > 0");
                    table.CheckConstraint("ck_release_policy_areas_policy_area_id", "length(btrim(policy_area_id)) > 0");
                    table.ForeignKey(
                        name: "FK_release_policy_areas_publication_releases_release_number",
                        column: x => x.release_number,
                        principalTable: "publication_releases",
                        principalColumn: "release_number",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "release_issue_excludes",
                columns: table => new
                {
                    release_number = table.Column<int>(type: "integer", nullable: false),
                    issue_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    position = table.Column<short>(type: "smallint", nullable: false),
                    text = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_release_issue_excludes", x => new { x.release_number, x.issue_id, x.position });
                    table.UniqueConstraint("AK_release_issue_excludes_release_number_issue_id_text", x => new { x.release_number, x.issue_id, x.text });
                    table.CheckConstraint("ck_release_issue_excludes_position", "position >= 0");
                    table.CheckConstraint("ck_release_issue_excludes_text", "length(btrim(text)) > 0");
                    table.ForeignKey(
                        name: "FK_release_issue_excludes_release_issues_release_number_issue_~",
                        columns: x => new { x.release_number, x.issue_id },
                        principalTable: "release_issues",
                        principalColumns: new[] { "release_number", "issue_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "release_issue_includes",
                columns: table => new
                {
                    release_number = table.Column<int>(type: "integer", nullable: false),
                    issue_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    position = table.Column<short>(type: "smallint", nullable: false),
                    text = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_release_issue_includes", x => new { x.release_number, x.issue_id, x.position });
                    table.UniqueConstraint("AK_release_issue_includes_release_number_issue_id_text", x => new { x.release_number, x.issue_id, x.text });
                    table.CheckConstraint("ck_release_issue_includes_position", "position >= 0");
                    table.CheckConstraint("ck_release_issue_includes_text", "length(btrim(text)) > 0");
                    table.ForeignKey(
                        name: "FK_release_issue_includes_release_issues_release_number_issue_~",
                        columns: x => new { x.release_number, x.issue_id },
                        principalTable: "release_issues",
                        principalColumns: new[] { "release_number", "issue_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "release_official_terms",
                columns: table => new
                {
                    release_number = table.Column<int>(type: "integer", nullable: false),
                    official_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    position = table.Column<short>(type: "smallint", nullable: false),
                    office = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    state = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    district = table.Column<int>(type: "integer", nullable: true),
                    party = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    caucus = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    start_on = table.Column<DateOnly>(type: "date", nullable: false),
                    end_on = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_release_official_terms", x => new { x.release_number, x.official_id, x.position });
                    table.CheckConstraint("ck_release_official_terms_dates", "end_on IS NULL OR end_on >= start_on");
                    table.CheckConstraint("ck_release_official_terms_district", "district IS NULL OR district >= 0");
                    table.CheckConstraint("ck_release_official_terms_kind", "kind IN ('Senator', 'Representative', 'Executive', 'Judicial', 'Other')");
                    table.CheckConstraint("ck_release_official_terms_office", "length(btrim(office)) > 0");
                    table.CheckConstraint("ck_release_official_terms_position", "position >= 0");
                    table.ForeignKey(
                        name: "FK_release_official_terms_release_officials_release_number_off~",
                        columns: x => new { x.release_number, x.official_id },
                        principalTable: "release_officials",
                        principalColumns: new[] { "release_number", "official_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.Sql(UpSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(DownSql);

            migrationBuilder.DropTable(
                name: "release_issue_excludes");

            migrationBuilder.DropTable(
                name: "release_issue_includes");

            migrationBuilder.DropTable(
                name: "release_official_terms");

            migrationBuilder.DropTable(
                name: "release_policy_areas");

            migrationBuilder.DropTable(
                name: "release_issues");

            migrationBuilder.DropTable(
                name: "release_officials");
        }
    }
}
