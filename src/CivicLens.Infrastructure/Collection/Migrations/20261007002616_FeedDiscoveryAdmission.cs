using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations
{
    /// <inheritdoc />
    public partial class FeedDiscoveryAdmission : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "collection_feed_candidate_jobs",
                columns: table => new
                {
                    candidate_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    source_id = table.Column<string>(type: "text", nullable: false),
                    url = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    job_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_collection_feed_candidate_jobs", x => x.candidate_hash);
                    table.ForeignKey(
                        name: "FK_collection_feed_candidate_jobs_collection_jobs_job_id",
                        column: x => x.job_id,
                        principalTable: "collection_jobs",
                        principalColumn: "job_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "collection_feed_discoveries",
                columns: table => new
                {
                    attempt_id = table.Column<string>(type: "text", nullable: false),
                    source_id = table.Column<string>(type: "text", nullable: false),
                    request_json = table.Column<string>(type: "text", nullable: false),
                    discovery_json = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_collection_feed_discoveries", x => x.attempt_id);
                    table.ForeignKey(
                        name: "FK_collection_feed_discoveries_collection_attempts_attempt_id",
                        column: x => x.attempt_id,
                        principalTable: "collection_attempts",
                        principalColumn: "attempt_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "collection_feed_admission_batches",
                columns: table => new
                {
                    idempotency_key = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    attempt_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    input_json = table.Column<string>(type: "text", nullable: false),
                    result_json = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_collection_feed_admission_batches", x => x.idempotency_key);
                    table.ForeignKey(
                        name: "FK_collection_feed_admission_batches_collection_feed_discoveri~",
                        column: x => x.attempt_id,
                        principalTable: "collection_feed_discoveries",
                        principalColumn: "attempt_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_collection_feed_admission_batches_attempt_id",
                table: "collection_feed_admission_batches",
                column: "attempt_id");

            migrationBuilder.CreateIndex(
                name: "IX_collection_feed_candidate_jobs_job_id",
                table: "collection_feed_candidate_jobs",
                column: "job_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "collection_feed_admission_batches");

            migrationBuilder.DropTable(
                name: "collection_feed_candidate_jobs");

            migrationBuilder.DropTable(
                name: "collection_feed_discoveries");
        }
    }
}
