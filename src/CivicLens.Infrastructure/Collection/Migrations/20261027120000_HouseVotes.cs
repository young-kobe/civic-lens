using CivicLens.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations;

[DbContext(typeof(CollectionAttemptDbContext))]
[Migration("20261027120000_HouseVotes")]
public partial class HouseVotes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "house_votes",
            columns: table => new
            {
                detail_job_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                source_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                congress = table.Column<int>(type: "integer", nullable: false),
                session_number = table.Column<int>(type: "integer", nullable: false),
                roll_call_number = table.Column<int>(type: "integer", nullable: false),
                update_date_utc_ticks = table.Column<long>(type: "bigint", nullable: false),
                bill_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                bill_number = table.Column<int>(type: "integer", nullable: true),
                extraction_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_house_votes", x => x.detail_job_id);
                table.CheckConstraint("ck_house_votes_bill", "(bill_type IS NULL) = (bill_number IS NULL) AND (bill_number IS NULL OR bill_number >= 1)");
                table.CheckConstraint("ck_house_votes_roll", "congress >= 1 AND session_number IN (1, 2) AND roll_call_number >= 1");
                table.CheckConstraint("ck_house_votes_update", "update_date_utc_ticks BETWEEN 0 AND 3155378975999999999");
                table.ForeignKey(
                    name: "FK_house_votes_collection_jobs_detail_job_id",
                    column: x => x.detail_job_id,
                    principalTable: "collection_jobs",
                    principalColumn: "job_id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_house_votes_document_extractions_extraction_id",
                    column: x => x.extraction_id,
                    principalTable: "document_extractions",
                    principalColumn: "extraction_id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "house_vote_candidates",
            columns: table => new
            {
                candidate_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                source_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                congress = table.Column<int>(type: "integer", nullable: false),
                session_number = table.Column<int>(type: "integer", nullable: false),
                roll_call_number = table.Column<int>(type: "integer", nullable: false),
                update_date_utc_ticks = table.Column<long>(type: "bigint", nullable: false),
                bill_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                bill_number = table.Column<int>(type: "integer", nullable: true),
                detail_job_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                members_job_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                detail_extraction_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                members_extraction_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                configuration_revision_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                created_at_utc_ticks = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_house_vote_candidates", x => x.candidate_id);
                table.CheckConstraint("ck_house_vote_candidates_bill", "(bill_type IS NULL) = (bill_number IS NULL) AND (bill_number IS NULL OR bill_number >= 1)");
                table.CheckConstraint("ck_house_vote_candidates_created", "created_at_utc_ticks BETWEEN 0 AND 3155378975999999999");
                table.CheckConstraint("ck_house_vote_candidates_id", "candidate_id ~ '^[0-9a-f]{64}$'");
                table.CheckConstraint("ck_house_vote_candidates_roll", "congress >= 1 AND session_number IN (1, 2) AND roll_call_number >= 1");
                table.CheckConstraint("ck_house_vote_candidates_update", "update_date_utc_ticks BETWEEN 0 AND 3155378975999999999");
                table.ForeignKey(
                    name: "FK_house_vote_candidates_collection_jobs_members_job_id",
                    column: x => x.members_job_id,
                    principalTable: "collection_jobs",
                    principalColumn: "job_id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_house_vote_candidates_document_extractions_detail_extraction_id",
                    column: x => x.detail_extraction_id,
                    principalTable: "document_extractions",
                    principalColumn: "extraction_id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_house_vote_candidates_document_extractions_members_extraction_id",
                    column: x => x.members_extraction_id,
                    principalTable: "document_extractions",
                    principalColumn: "extraction_id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_house_vote_candidates_house_votes_detail_job_id",
                    column: x => x.detail_job_id,
                    principalTable: "house_votes",
                    principalColumn: "detail_job_id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_house_vote_candidates_configuration_revision",
                    column: x => x.configuration_revision_id,
                    principalTable: "collection_configuration_revisions",
                    principalColumn: "revision_id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_house_vote_candidates_configuration_revision_id",
            table: "house_vote_candidates",
            column: "configuration_revision_id");

        migrationBuilder.CreateIndex(
            name: "IX_house_vote_candidates_detail_extraction_id",
            table: "house_vote_candidates",
            column: "detail_extraction_id");

        migrationBuilder.CreateIndex(
            name: "IX_house_vote_candidates_members_extraction_id",
            table: "house_vote_candidates",
            column: "members_extraction_id");

        migrationBuilder.CreateIndex(
            name: "IX_house_vote_candidates_members_job_id",
            table: "house_vote_candidates",
            column: "members_job_id");

        migrationBuilder.CreateIndex(
            name: "ux_house_vote_candidates_detail_job",
            table: "house_vote_candidates",
            column: "detail_job_id",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_house_votes_extraction_id",
            table: "house_votes",
            column: "extraction_id");

        migrationBuilder.CreateIndex(
            name: "ux_house_votes_version",
            table: "house_votes",
            columns: new[] { "source_id", "congress", "session_number", "roll_call_number", "update_date_utc_ticks" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "house_vote_candidates");

        migrationBuilder.DropTable(
            name: "house_votes");
    }
}
