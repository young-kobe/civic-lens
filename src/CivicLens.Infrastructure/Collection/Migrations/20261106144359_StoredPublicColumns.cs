using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations
{
    /// <inheritdoc />
    public partial class StoredPublicColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "alignment",
                table: "vote_revisions",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "columns_written",
                table: "vote_revisions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "democrat_majority",
                table: "vote_revisions",
                type: "character varying(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "republican_majority",
                table: "vote_revisions",
                type: "character varying(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "party",
                table: "vote_member_list_entries",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "photo_path",
                table: "release_officials",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "congress",
                table: "published_entries",
                type: "integer",
                nullable: true);

            migrationBuilder.Sql("ALTER TABLE vote_revisions ADD CONSTRAINT ck_vote_revisions_alignment CHECK (alignment IS NULL OR alignment IN ('Split', 'PartiesAgree', 'Mixed'));");
            migrationBuilder.Sql("ALTER TABLE vote_revisions ADD CONSTRAINT ck_vote_revisions_columns_written CHECK (columns_written OR (alignment IS NULL AND democrat_majority IS NULL AND republican_majority IS NULL));");
            migrationBuilder.Sql("ALTER TABLE vote_revisions ADD CONSTRAINT ck_vote_revisions_majorities CHECK ((democrat_majority IS NULL OR democrat_majority IN ('Yea', 'Nay')) AND (republican_majority IS NULL OR republican_majority IN ('Yea', 'Nay')));");
            migrationBuilder.Sql("ALTER TABLE vote_member_list_entries ADD CONSTRAINT ck_vote_member_list_entries_party CHECK (party IS NULL OR length(btrim(party)) > 0);");
            migrationBuilder.Sql("ALTER TABLE release_officials ADD CONSTRAINT ck_release_officials_photo_path CHECK (photo_path IS NULL OR length(btrim(photo_path)) > 0);");
            migrationBuilder.Sql("ALTER TABLE published_entries ADD CONSTRAINT ck_published_entries_congress CHECK (congress IS NULL OR congress >= 1);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_vote_revisions_alignment",
                table: "vote_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_vote_revisions_columns_written",
                table: "vote_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_vote_revisions_majorities",
                table: "vote_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_vote_member_list_entries_party",
                table: "vote_member_list_entries");

            migrationBuilder.DropCheckConstraint(
                name: "ck_release_officials_photo_path",
                table: "release_officials");

            migrationBuilder.DropCheckConstraint(
                name: "ck_published_entries_congress",
                table: "published_entries");

            migrationBuilder.DropColumn(
                name: "alignment",
                table: "vote_revisions");

            migrationBuilder.DropColumn(
                name: "columns_written",
                table: "vote_revisions");

            migrationBuilder.DropColumn(
                name: "democrat_majority",
                table: "vote_revisions");

            migrationBuilder.DropColumn(
                name: "republican_majority",
                table: "vote_revisions");

            migrationBuilder.DropColumn(
                name: "party",
                table: "vote_member_list_entries");

            migrationBuilder.DropColumn(
                name: "photo_path",
                table: "release_officials");

            migrationBuilder.DropColumn(
                name: "congress",
                table: "published_entries");
        }
    }
}
