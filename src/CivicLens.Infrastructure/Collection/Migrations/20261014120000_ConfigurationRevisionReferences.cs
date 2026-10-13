using CivicLens.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations;

[DbContext(typeof(CollectionAttemptDbContext))]
[Migration("20261014120000_ConfigurationRevisionReferences")]
public partial class ConfigurationRevisionReferences : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "collection_configuration_revisions",
            columns: table => new
            {
                revision_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                json = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_collection_configuration_revisions", x => x.revision_id);
                table.CheckConstraint("ck_collection_configuration_revisions_id", "revision_id ~ '^[0-9a-f]{64}$'");
            });

        migrationBuilder.AddColumn<string>(name: "configuration_revision_id", table: "collection_jobs",
            type: "character varying(64)", maxLength: 64, nullable: true);
        migrationBuilder.AddColumn<string>(name: "configuration_revision_id", table: "collection_admission_batches",
            type: "character varying(64)", maxLength: 64, nullable: true);

        migrationBuilder.Sql("""
            INSERT INTO collection_configuration_revisions (revision_id, json, created_at)
            SELECT DISTINCT ON (revision_id) revision_id, json, created_at
              FROM (SELECT revision ->> 'id' AS revision_id, revision ->> 'json' AS json, created_at
                      FROM (SELECT definition_json::jsonb -> 'configurationRevision' AS revision, created_at
                              FROM collection_jobs
                            UNION ALL
                            SELECT input_json::jsonb -> 'articleTemplate' -> 'configurationRevision', created_at
                              FROM collection_admission_batches) AS embedded
                     WHERE revision IS NOT NULL
                    UNION ALL
                    SELECT configuration_revision_id, configuration_json, 0
                      FROM collection_source_schedules) AS legacy
             ORDER BY revision_id, created_at
            ON CONFLICT (revision_id) DO NOTHING;

            UPDATE collection_jobs
               SET configuration_revision_id = definition_json::jsonb -> 'configurationRevision' ->> 'id',
                   definition_json = (definition_json::jsonb - 'configurationRevision')::text
             WHERE definition_json::jsonb ? 'configurationRevision';

            UPDATE collection_admission_batches
               SET configuration_revision_id = input_json::jsonb -> 'articleTemplate' -> 'configurationRevision' ->> 'id',
                   input_json = (input_json::jsonb #- '{articleTemplate,configurationRevision}')::text
             WHERE input_json::jsonb -> 'articleTemplate' ? 'configurationRevision';
            """);

        migrationBuilder.DropColumn(name: "configuration_json", table: "collection_source_schedules");

        migrationBuilder.CreateIndex(name: "IX_collection_jobs_configuration_revision_id",
            table: "collection_jobs", column: "configuration_revision_id");
        migrationBuilder.CreateIndex(name: "IX_collection_admission_batches_configuration_revision_id",
            table: "collection_admission_batches", column: "configuration_revision_id");
        migrationBuilder.CreateIndex(name: "IX_collection_source_schedules_configuration_revision_id",
            table: "collection_source_schedules", column: "configuration_revision_id");
        migrationBuilder.AddForeignKey(name: "fk_collection_jobs_configuration_revision",
            table: "collection_jobs", column: "configuration_revision_id",
            principalTable: "collection_configuration_revisions", principalColumn: "revision_id", onDelete: ReferentialAction.Restrict);
        migrationBuilder.AddForeignKey(name: "fk_collection_admission_batches_configuration_revision",
            table: "collection_admission_batches", column: "configuration_revision_id",
            principalTable: "collection_configuration_revisions", principalColumn: "revision_id", onDelete: ReferentialAction.Restrict);
        migrationBuilder.AddForeignKey(name: "fk_collection_source_schedules_configuration_revision",
            table: "collection_source_schedules", column: "configuration_revision_id",
            principalTable: "collection_configuration_revisions", principalColumn: "revision_id", onDelete: ReferentialAction.Restrict);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(name: "fk_collection_jobs_configuration_revision",
            table: "collection_jobs");
        migrationBuilder.DropForeignKey(name: "fk_collection_admission_batches_configuration_revision",
            table: "collection_admission_batches");
        migrationBuilder.DropForeignKey(name: "fk_collection_source_schedules_configuration_revision",
            table: "collection_source_schedules");
        migrationBuilder.DropIndex(name: "IX_collection_jobs_configuration_revision_id", table: "collection_jobs");
        migrationBuilder.DropIndex(name: "IX_collection_admission_batches_configuration_revision_id", table: "collection_admission_batches");
        migrationBuilder.DropIndex(name: "IX_collection_source_schedules_configuration_revision_id", table: "collection_source_schedules");
        migrationBuilder.AddColumn<string>(name: "configuration_json", table: "collection_source_schedules",
            type: "text", nullable: false, defaultValue: "");
        migrationBuilder.Sql("""
            UPDATE collection_source_schedules AS schedule
               SET configuration_json = revision.json
              FROM collection_configuration_revisions AS revision
             WHERE schedule.configuration_revision_id = revision.revision_id;

            UPDATE collection_jobs AS job
               SET definition_json = (job.definition_json::jsonb
                   || jsonb_build_object('configurationRevision',
                       jsonb_build_object('id', revision.revision_id, 'json', revision.json)))::text
              FROM collection_configuration_revisions AS revision
             WHERE job.configuration_revision_id = revision.revision_id;

            UPDATE collection_admission_batches AS batch
               SET input_json = jsonb_set(batch.input_json::jsonb, '{articleTemplate,configurationRevision}',
                   jsonb_build_object('id', revision.revision_id, 'json', revision.json))::text
              FROM collection_configuration_revisions AS revision
             WHERE batch.configuration_revision_id = revision.revision_id;
            """);
        migrationBuilder.DropColumn(name: "configuration_revision_id", table: "collection_jobs");
        migrationBuilder.DropColumn(name: "configuration_revision_id", table: "collection_admission_batches");
        migrationBuilder.DropTable(name: "collection_configuration_revisions");
    }
}
