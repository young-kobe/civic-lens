using CivicLens.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations;

[DbContext(typeof(CollectionAttemptDbContext))]
[Migration("20261022160000_DerivedJobCharges")]
public partial class DerivedJobCharges : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint("ck_job_budget", "collection_jobs");
        migrationBuilder.DropColumn("charged_requests", "collection_jobs");
        migrationBuilder.DropColumn("charged_bytes", "collection_jobs");
        migrationBuilder.DropColumn("charged_seconds", "collection_jobs");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>("charged_requests", "collection_jobs", "integer", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<long>("charged_bytes", "collection_jobs", "bigint", nullable: false, defaultValue: 0L);
        migrationBuilder.AddColumn<int>("charged_seconds", "collection_jobs", "integer", nullable: false, defaultValue: 0);
        migrationBuilder.Sql("""
            UPDATE collection_jobs job
               SET charged_requests = charges.requests, charged_bytes = charges.bytes, charged_seconds = charges.seconds
              FROM (SELECT job_id,
                           sum(coalesce((resolution_json::jsonb #>> '{receipt,requestCount}')::int,
                                        (request_json::jsonb ->> 'maxRequests')::int)) AS requests,
                           sum(coalesce((resolution_json::jsonb #>> '{receipt,bytesReceived}')::bigint,
                                        (request_json::jsonb ->> 'maxBytes')::bigint)) AS bytes,
                           sum((request_json::jsonb ->> 'timeoutSeconds')::int) AS seconds
                      FROM collection_job_attempts GROUP BY job_id) charges
             WHERE charges.job_id = job.job_id;
            """);
        migrationBuilder.AddCheckConstraint("ck_job_budget", "collection_jobs",
            "charged_requests >= 0 AND charged_bytes >= 0 AND charged_seconds >= 0");
    }
}
