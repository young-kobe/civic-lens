using CivicLens.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations;

[DbContext(typeof(CollectionAttemptDbContext))]
[Migration("20261023120000_JobSourceColumns")]
public partial class JobSourceColumns : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "source_id", table: "collection_jobs", type: "text", nullable: true);
        migrationBuilder.AddColumn<bool>(name: "is_root", table: "collection_jobs", type: "boolean", nullable: true);

        // The backfill applies the rule of CollectionJobDefinition.IsRoot to the revision of each job.
        migrationBuilder.Sql("""
            UPDATE collection_jobs AS job
               SET source_id = job.definition_json::jsonb ->> 'sourceId',
                   is_root = (job.definition_json::jsonb ->> 'apiResource') IS NOT DISTINCT FROM 'billList'
                       OR EXISTS (
                           SELECT 1
                             FROM collection_configuration_revisions AS revision
                            CROSS JOIN LATERAL jsonb_array_elements(revision.json::jsonb -> 'sources') AS source
                            WHERE revision.revision_id = job.configuration_revision_id
                              AND source ->> 'id' = job.definition_json::jsonb ->> 'sourceId'
                              AND source ->> 'url' = job.definition_json::jsonb ->> 'url');

            DROP INDEX ix_collection_jobs_active_source_id;
            DROP INDEX ix_collection_jobs_source_created;
            """);

        migrationBuilder.AlterColumn<string>(name: "source_id", table: "collection_jobs", type: "text", nullable: false,
            oldClrType: typeof(string), oldType: "text", oldNullable: true);
        migrationBuilder.AlterColumn<bool>(name: "is_root", table: "collection_jobs", type: "boolean", nullable: false,
            oldClrType: typeof(bool), oldType: "boolean", oldNullable: true);

        migrationBuilder.CreateIndex(
            name: "ix_collection_jobs_root_source_created",
            table: "collection_jobs",
            columns: new[] { "source_id", "created_at", "job_id" },
            descending: new[] { false, true, false },
            filter: "is_root");
        migrationBuilder.CreateIndex(
            name: "ix_collection_jobs_active_root_source_id",
            table: "collection_jobs",
            column: "source_id",
            filter: "is_root AND state IN ('Pending','Running','WaitingToRetry')");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "ix_collection_jobs_active_root_source_id", table: "collection_jobs");
        migrationBuilder.DropIndex(name: "ix_collection_jobs_root_source_created", table: "collection_jobs");
        migrationBuilder.DropColumn(name: "is_root", table: "collection_jobs");
        migrationBuilder.DropColumn(name: "source_id", table: "collection_jobs");
        migrationBuilder.Sql("""
            CREATE INDEX ix_collection_jobs_active_source_id
                ON collection_jobs ((definition_json::jsonb ->> 'sourceId'))
                WHERE state IN ('Pending','Running','WaitingToRetry');
            CREATE INDEX ix_collection_jobs_source_created
                ON collection_jobs ((definition_json::jsonb ->> 'sourceId'), created_at DESC);
            """);
    }
}
