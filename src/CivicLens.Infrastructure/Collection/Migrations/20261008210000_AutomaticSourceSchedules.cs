using CivicLens.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations;

[DbContext(typeof(CollectionAttemptDbContext))]
[Migration("20261008210000_AutomaticSourceSchedules")]
public partial class AutomaticSourceSchedules : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "collection_source_schedules",
            columns: table => new
            {
                source_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                configuration_revision_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                configuration_json = table.Column<string>(type: "text", nullable: false),
                interval_seconds = table.Column<int>(type: "integer", nullable: false),
                next_due_utc_ticks = table.Column<long>(type: "bigint", nullable: false),
                enabled = table.Column<bool>(type: "boolean", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_collection_source_schedules", row => row.source_id);
                table.CheckConstraint("ck_collection_source_schedules_revision", "configuration_revision_id ~ '^[0-9a-f]{64}$'");
                table.CheckConstraint("ck_collection_source_schedules_interval", "interval_seconds BETWEEN 60 AND 604800");
                table.CheckConstraint("ck_collection_source_schedules_due", "next_due_utc_ticks BETWEEN 0 AND 3155378975999999999");
            });
        migrationBuilder.CreateIndex("IX_collection_source_schedules_enabled_next_due_utc_ticks_source_id",
            "collection_source_schedules", new[] { "enabled", "next_due_utc_ticks", "source_id" });
        migrationBuilder.Sql("""
            CREATE INDEX ix_collection_jobs_active_source_id
                ON collection_jobs ((definition_json::jsonb ->> 'sourceId'))
                WHERE state IN ('Pending','Running','WaitingToRetry');

            CREATE TRIGGER trg_collection_source_schedules_notify
            AFTER INSERT OR DELETE OR UPDATE OF enabled, configuration_revision_id, interval_seconds, next_due_utc_ticks
            ON collection_source_schedules
            FOR EACH ROW EXECUTE FUNCTION civic_lens_notify_work_change();

            INSERT INTO evidence_processing (job_id, attempt_id, source_id, requested_url, stage, status,
                fence, attempts, admitted_count, deferred_count, duplicate_count)
            SELECT job_id, 'prepare', definition_json::jsonb ->> 'sourceId', definition_json::jsonb ->> 'url',
                'Preparation', 'Pending', 0, 0, 0, 0, 0
              FROM collection_jobs
             WHERE state = 'Succeeded'
            ON CONFLICT (job_id, attempt_id) DO NOTHING;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP TRIGGER IF EXISTS trg_collection_source_schedules_notify ON collection_source_schedules;
            DROP INDEX IF EXISTS ix_collection_jobs_active_source_id;
            """);
        migrationBuilder.DropTable("collection_source_schedules");
    }
}
