using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations
{
    /// <inheritdoc />
    public partial class InitialCollectionEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "collection_captures",
                columns: table => new
                {
                    sha256 = table.Column<string>(type: "text", nullable: false),
                    byte_length = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_collection_captures", x => x.sha256);
                    table.CheckConstraint("ck_collection_captures_byte_length", "byte_length >= 0");
                    table.CheckConstraint("ck_collection_captures_sha256", "sha256 ~ '^[0-9a-f]{64}$'");
                });

            migrationBuilder.CreateTable(
                name: "collection_attempts",
                columns: table => new
                {
                    attempt_id = table.Column<string>(type: "text", nullable: false),
                    source_id = table.Column<string>(type: "text", nullable: false),
                    requested_url = table.Column<string>(type: "text", nullable: false),
                    final_url = table.Column<string>(type: "text", nullable: false),
                    observed_at_utc_ticks = table.Column<long>(type: "bigint", nullable: false),
                    outcome = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    has_response = table.Column<bool>(type: "boolean", nullable: false),
                    response_status_code = table.Column<int>(type: "integer", nullable: true),
                    response_etag = table.Column<string>(type: "text", nullable: true),
                    response_last_modified_utc_ticks = table.Column<long>(type: "bigint", nullable: true),
                    response_content_type = table.Column<string>(type: "text", nullable: true),
                    response_content_encodings = table.Column<string[]>(type: "text[]", nullable: true),
                    has_sent_validators = table.Column<bool>(type: "boolean", nullable: false),
                    sent_etag = table.Column<string>(type: "text", nullable: true),
                    sent_last_modified_utc_ticks = table.Column<long>(type: "bigint", nullable: true),
                    failure_code = table.Column<string>(type: "text", nullable: true),
                    retry_delay_ticks = table.Column<long>(type: "bigint", nullable: true),
                    capture_sha256 = table.Column<string>(type: "text", nullable: true),
                    prior_capture_attempt_id = table.Column<string>(type: "text", nullable: true),
                    prior_capture_sha256 = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_collection_attempts", x => x.attempt_id);
                    table.CheckConstraint("ck_collection_attempts_identity", "length(btrim(attempt_id)) > 0 AND length(btrim(source_id)) > 0 AND length(btrim(requested_url)) > 0 AND length(btrim(final_url)) > 0");
                    table.CheckConstraint("ck_collection_attempts_observed_ticks", "observed_at_utc_ticks BETWEEN 0 AND 3155378975999999999");
                    table.CheckConstraint("ck_collection_attempts_outcome", "(outcome = 'captured' AND has_response AND response_status_code IS NOT NULL AND response_status_code = 200 AND capture_sha256 IS NOT NULL AND failure_code IS NULL AND retry_delay_ticks IS NULL AND prior_capture_attempt_id IS NULL AND prior_capture_sha256 IS NULL)\nOR (outcome = 'notModified' AND has_response AND has_sent_validators AND response_status_code IS NOT NULL AND response_status_code = 304 AND capture_sha256 IS NULL AND failure_code IS NULL AND retry_delay_ticks IS NULL)\nOR (outcome = 'failed' AND failure_code IS NOT NULL AND retry_delay_ticks IS NULL AND capture_sha256 IS NULL AND prior_capture_attempt_id IS NULL AND prior_capture_sha256 IS NULL)\nOR (outcome = 'deferred' AND failure_code IS NOT NULL AND capture_sha256 IS NULL AND prior_capture_attempt_id IS NULL AND prior_capture_sha256 IS NULL)");
                    table.CheckConstraint("ck_collection_attempts_prior_outcome", "prior_capture_attempt_id IS NULL OR (outcome = 'notModified' AND prior_capture_attempt_id <> attempt_id)");
                    table.CheckConstraint("ck_collection_attempts_prior_pair", "(prior_capture_attempt_id IS NULL) = (prior_capture_sha256 IS NULL)");
                    table.CheckConstraint("ck_collection_attempts_response", "(has_response AND response_status_code IS NOT NULL AND response_status_code BETWEEN 100 AND 599 AND response_content_encodings IS NOT NULL AND array_position(response_content_encodings, NULL) IS NULL)\nOR (NOT has_response AND response_status_code IS NULL AND response_etag IS NULL AND response_last_modified_utc_ticks IS NULL AND response_content_type IS NULL AND response_content_encodings IS NULL)");
                    table.CheckConstraint("ck_collection_attempts_response_ticks", "response_last_modified_utc_ticks IS NULL OR response_last_modified_utc_ticks BETWEEN 0 AND 3155378975999999999");
                    table.CheckConstraint("ck_collection_attempts_retry_ticks", "retry_delay_ticks IS NULL OR retry_delay_ticks >= 0");
                    table.CheckConstraint("ck_collection_attempts_sent_ticks", "sent_last_modified_utc_ticks IS NULL OR sent_last_modified_utc_ticks BETWEEN 0 AND 3155378975999999999");
                    table.CheckConstraint("ck_collection_attempts_validators", "(has_sent_validators OR (sent_etag IS NULL AND sent_last_modified_utc_ticks IS NULL))");
                    table.ForeignKey(
                        name: "FK_collection_attempts_collection_attempts_prior_capture_attem~",
                        column: x => x.prior_capture_attempt_id,
                        principalTable: "collection_attempts",
                        principalColumn: "attempt_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_collection_attempts_collection_captures_capture_sha256",
                        column: x => x.capture_sha256,
                        principalTable: "collection_captures",
                        principalColumn: "sha256",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_collection_attempts_capture",
                table: "collection_attempts",
                column: "capture_sha256");

            migrationBuilder.CreateIndex(
                name: "ix_collection_attempts_prior_capture",
                table: "collection_attempts",
                columns: new[] { "source_id", "observed_at_utc_ticks", "attempt_id" },
                filter: "outcome = 'captured'");

            migrationBuilder.CreateIndex(
                name: "ix_collection_attempts_prior_capture_link",
                table: "collection_attempts",
                column: "prior_capture_attempt_id");

            migrationBuilder.CreateIndex(
                name: "ux_collection_attempts_id_capture",
                table: "collection_attempts",
                columns: new[] { "attempt_id", "capture_sha256" },
                unique: true);

            migrationBuilder.Sql("""
                ALTER TABLE collection_attempts
                ADD CONSTRAINT fk_collection_attempts_prior_capture_observation
                FOREIGN KEY (prior_capture_attempt_id, prior_capture_sha256)
                REFERENCES collection_attempts (attempt_id, capture_sha256)
                ON DELETE RESTRICT
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "collection_attempts");

            migrationBuilder.DropTable(
                name: "collection_captures");
        }
    }
}
