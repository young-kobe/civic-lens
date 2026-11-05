using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations
{
    /// <inheritdoc />
    public partial class PeopleTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "person_registry_sequence");

            migrationBuilder.CreateTable(
                name: "people",
                columns: table => new
                {
                    person_id = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    bioguide_id = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: true),
                    wikidata_id = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    govtrack_id = table.Column<int>(type: "integer", nullable: true),
                    created_at_utc_ticks = table.Column<long>(type: "bigint", nullable: false),
                    created_by_attempt_id = table.Column<string>(type: "text", nullable: true),
                    created_by_actor = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_people", x => x.person_id);
                    table.CheckConstraint("ck_people_created_at_utc_ticks", "created_at_utc_ticks BETWEEN 0 AND 3155378975999999999");
                    table.CheckConstraint("ck_people_creator", "num_nonnulls(created_by_attempt_id, created_by_actor) = 1");
                    table.CheckConstraint("ck_people_identity", "person_id ~ '^[a-z0-9]+(-[a-z0-9]+)*$' AND (bioguide_id IS NULL OR bioguide_id ~ '^[A-Z][0-9]{6}$') AND (wikidata_id IS NULL OR wikidata_id ~ '^Q[1-9][0-9]*$') AND (govtrack_id IS NULL OR govtrack_id > 0)");
                    table.ForeignKey(
                        name: "FK_people_collection_attempts_created_by_attempt_id",
                        column: x => x.created_by_attempt_id,
                        principalTable: "collection_attempts",
                        principalColumn: "attempt_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "person_office_revisions",
                columns: table => new
                {
                    person_id = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    revision_number = table.Column<int>(type: "integer", nullable: false),
                    note = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    actor_subject = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    recorded_at_utc_ticks = table.Column<long>(type: "bigint", nullable: false),
                    registry_sequence = table.Column<long>(type: "bigint", nullable: false, defaultValueSql: "nextval('person_registry_sequence')")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_person_office_revisions", x => new { x.person_id, x.revision_number });
                    table.CheckConstraint("ck_person_office_revisions_recorded_at_utc_ticks", "recorded_at_utc_ticks BETWEEN 0 AND 3155378975999999999");
                    table.CheckConstraint("ck_person_office_revisions_shape", "revision_number >= 1 AND length(btrim(note)) > 0 AND length(btrim(actor_subject)) > 0");
                    table.ForeignKey(
                        name: "FK_person_office_revisions_people_person_id",
                        column: x => x.person_id,
                        principalTable: "people",
                        principalColumn: "person_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "person_setting_revisions",
                columns: table => new
                {
                    person_id = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    revision_number = table.Column<int>(type: "integer", nullable: false),
                    withhold_photo = table.Column<bool>(type: "boolean", nullable: false),
                    website = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    note = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    actor_subject = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    recorded_at_utc_ticks = table.Column<long>(type: "bigint", nullable: false),
                    registry_sequence = table.Column<long>(type: "bigint", nullable: false, defaultValueSql: "nextval('person_registry_sequence')")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_person_setting_revisions", x => new { x.person_id, x.revision_number });
                    table.CheckConstraint("ck_person_setting_revisions_recorded_at_utc_ticks", "recorded_at_utc_ticks BETWEEN 0 AND 3155378975999999999");
                    table.CheckConstraint("ck_person_setting_revisions_shape", "revision_number >= 1 AND (website IS NULL OR length(btrim(website)) > 0) AND length(btrim(note)) > 0 AND length(btrim(actor_subject)) > 0");
                    table.ForeignKey(
                        name: "FK_person_setting_revisions_people_person_id",
                        column: x => x.person_id,
                        principalTable: "people",
                        principalColumn: "person_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "person_office_revision_terms",
                columns: table => new
                {
                    person_id = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    revision_number = table.Column<int>(type: "integer", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    starts_on = table.Column<DateOnly>(type: "date", nullable: false),
                    ends_on = table.Column<DateOnly>(type: "date", nullable: true),
                    party = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_person_office_revision_terms", x => new { x.person_id, x.revision_number, x.position });
                    table.CheckConstraint("ck_person_office_revision_terms_kind", "kind IN ('Executive', 'Judicial')");
                    table.CheckConstraint("ck_person_office_revision_terms_shape", "position >= 0 AND length(btrim(title)) > 0 AND (party IS NULL OR length(btrim(party)) > 0) AND (ends_on IS NULL OR ends_on >= starts_on)");
                    table.ForeignKey(
                        name: "FK_person_office_revision_terms_person_office_revisions_person~",
                        columns: x => new { x.person_id, x.revision_number },
                        principalTable: "person_office_revisions",
                        principalColumns: new[] { "person_id", "revision_number" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_people_bioguide_id",
                table: "people",
                column: "bioguide_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_people_created_by_attempt_id",
                table: "people",
                column: "created_by_attempt_id");

            migrationBuilder.CreateIndex(
                name: "IX_people_govtrack_id",
                table: "people",
                column: "govtrack_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_people_wikidata_id",
                table: "people",
                column: "wikidata_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_person_office_revisions_registry_sequence",
                table: "person_office_revisions",
                column: "registry_sequence",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_person_setting_revisions_registry_sequence",
                table: "person_setting_revisions",
                column: "registry_sequence",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "person_office_revision_terms");

            migrationBuilder.DropTable(
                name: "person_setting_revisions");

            migrationBuilder.DropTable(
                name: "person_office_revisions");

            migrationBuilder.DropTable(
                name: "people");

            migrationBuilder.DropSequence(
                name: "person_registry_sequence");
        }
    }
}
