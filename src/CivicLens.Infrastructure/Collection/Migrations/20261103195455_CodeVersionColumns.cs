using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations
{
    /// <inheritdoc />
    public partial class CodeVersionColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "code_version",
                table: "publication_releases",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "code_version",
                table: "action_revisions",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "code_version",
                table: "executive_action_revisions",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_publication_releases_code_version",
                table: "publication_releases",
                sql: "code_version ~ '^[0-9a-f]{40}$'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_action_revisions_code_version",
                table: "action_revisions",
                sql: "code_version ~ '^[0-9a-f]{40}$'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_executive_action_revisions_code_version",
                table: "executive_action_revisions",
                sql: "code_version ~ '^[0-9a-f]{40}$'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_action_revisions_code_version_author",
                table: "action_revisions",
                sql: "code_version IS NULL OR author_kind = 'Code'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_publication_releases_code_version",
                table: "publication_releases");

            migrationBuilder.DropCheckConstraint(
                name: "ck_action_revisions_code_version",
                table: "action_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_executive_action_revisions_code_version",
                table: "executive_action_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_action_revisions_code_version_author",
                table: "action_revisions");

            migrationBuilder.DropColumn(
                name: "code_version",
                table: "publication_releases");

            migrationBuilder.DropColumn(
                name: "code_version",
                table: "action_revisions");

            migrationBuilder.DropColumn(
                name: "code_version",
                table: "executive_action_revisions");
        }
    }
}
