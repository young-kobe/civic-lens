using CivicLens.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations;

[DbContext(typeof(CollectionAttemptDbContext))]
[Migration("20261022130000_CollectionJobClaimIndex")]
public partial class CollectionJobClaimIndex : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateIndex(
            name: "ix_collection_jobs_active_created_at_job_id",
            table: "collection_jobs",
            columns: new[] { "created_at", "job_id" },
            filter: "state IN ('Pending','Running','WaitingToRetry')");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ix_collection_jobs_active_created_at_job_id",
            table: "collection_jobs");
    }
}
