using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations
{
    /// <inheritdoc />
    public partial class DurableCollectionJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "collection_jobs",
                columns: table => new
                {
                    job_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    definition_json = table.Column<string>(type: "text", nullable: false),
                    state = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<long>(type: "bigint", nullable: false),
                    retry_at = table.Column<long>(type: "bigint", nullable: true),
                    cancellation_requested = table.Column<bool>(type: "boolean", nullable: false),
                    charged_requests = table.Column<int>(type: "integer", nullable: false),
                    charged_bytes = table.Column<long>(type: "bigint", nullable: false),
                    charged_seconds = table.Column<int>(type: "integer", nullable: false),
                    lease_token = table.Column<string>(type: "text", nullable: true),
                    lease_fence = table.Column<long>(type: "bigint", nullable: false),
                    lease_expires_at = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_collection_jobs", x => x.job_id);
                    table.CheckConstraint("ck_job_budget", "charged_requests >= 0 AND charged_bytes >= 0 AND charged_seconds >= 0");
                    table.CheckConstraint("ck_job_lease", "(lease_token IS NULL) = (lease_expires_at IS NULL)");
                    table.CheckConstraint("ck_job_state", "state IN ('Pending','Running','WaitingToRetry','Succeeded','Failed','Cancelled')");
                });

            migrationBuilder.CreateTable(
                name: "collection_job_attempts",
                columns: table => new
                {
                    attempt_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    job_id = table.Column<string>(type: "character varying(128)", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    request_json = table.Column<string>(type: "text", nullable: false),
                    started_at = table.Column<long>(type: "bigint", nullable: false),
                    resolution_json = table.Column<string>(type: "text", nullable: true),
                    completed_at = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_collection_job_attempts", x => x.attempt_id);
                    table.CheckConstraint("ck_job_attempt_resolution", "(resolution_json IS NULL) = (completed_at IS NULL)");
                    table.CheckConstraint("ck_job_attempt_sequence", "sequence > 0");
                    table.ForeignKey(
                        name: "FK_collection_job_attempts_collection_jobs_job_id",
                        column: x => x.job_id,
                        principalTable: "collection_jobs",
                        principalColumn: "job_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "collection_collector_slot",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    job_id = table.Column<string>(type: "character varying(128)", nullable: true),
                    attempt_id = table.Column<string>(type: "character varying(128)", nullable: true),
                    token = table.Column<string>(type: "text", nullable: true),
                    fence = table.Column<long>(type: "bigint", nullable: false),
                    expires_at = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_collection_collector_slot", x => x.id);
                    table.CheckConstraint("ck_collector_lease", "(token IS NULL AND expires_at IS NULL AND job_id IS NULL AND attempt_id IS NULL) OR (token IS NOT NULL AND expires_at IS NOT NULL AND job_id IS NOT NULL AND attempt_id IS NOT NULL)");
                    table.CheckConstraint("ck_collector_singleton", "id = 1");
                    table.ForeignKey(
                        name: "FK_collection_collector_slot_collection_job_attempts_attempt_id",
                        column: x => x.attempt_id,
                        principalTable: "collection_job_attempts",
                        principalColumn: "attempt_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_collection_collector_slot_collection_jobs_job_id",
                        column: x => x.job_id,
                        principalTable: "collection_jobs",
                        principalColumn: "job_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "collection_job_origins",
                columns: table => new
                {
                    origin = table.Column<string>(type: "text", nullable: false),
                    not_before = table.Column<long>(type: "bigint", nullable: false),
                    unresolved_attempt_id = table.Column<string>(type: "character varying(128)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_collection_job_origins", x => x.origin);
                    table.ForeignKey(
                        name: "FK_collection_job_origins_collection_job_attempts_unresolved_a~",
                        column: x => x.unresolved_attempt_id,
                        principalTable: "collection_job_attempts",
                        principalColumn: "attempt_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_collection_collector_slot_attempt_id",
                table: "collection_collector_slot",
                column: "attempt_id");

            migrationBuilder.CreateIndex(
                name: "IX_collection_collector_slot_job_id",
                table: "collection_collector_slot",
                column: "job_id");

            migrationBuilder.CreateIndex(
                name: "IX_collection_job_attempts_job_id",
                table: "collection_job_attempts",
                column: "job_id",
                unique: true,
                filter: "resolution_json IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_collection_job_attempts_job_id_sequence",
                table: "collection_job_attempts",
                columns: new[] { "job_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_collection_job_origins_unresolved_attempt_id",
                table: "collection_job_origins",
                column: "unresolved_attempt_id");

            migrationBuilder.CreateIndex(
                name: "IX_collection_jobs_created_at_job_id",
                table: "collection_jobs",
                columns: new[] { "created_at", "job_id" });

            migrationBuilder.CreateIndex(
                name: "IX_collection_jobs_idempotency_key",
                table: "collection_jobs",
                column: "idempotency_key",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "collection_collector_slot");

            migrationBuilder.DropTable(
                name: "collection_job_origins");

            migrationBuilder.DropTable(
                name: "collection_job_attempts");

            migrationBuilder.DropTable(
                name: "collection_jobs");
        }
    }
}
