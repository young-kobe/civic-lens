using CivicLens.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations;

[DbContext(typeof(CollectionAttemptDbContext))]
[Migration("20261018120000_ActionCandidates")]
public partial class ActionCandidates : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "cosponsor_count",
            table: "bill_gates",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddCheckConstraint(
            name: "ck_bill_gates_cosponsor_count",
            table: "bill_gates",
            sql: "cosponsor_count >= 0");

        migrationBuilder.CreateTable(
            name: "bill_pages",
            columns: table => new
            {
                job_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                detail_job_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                page_order = table.Column<int>(type: "integer", nullable: false),
                extraction_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                closes_chain = table.Column<bool>(type: "boolean", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_bill_pages", x => x.job_id);
                table.CheckConstraint("ck_bill_pages_order", "page_order >= 0");
                table.CheckConstraint("ck_bill_pages_role", "role IN ('Detail','Cosponsors','Actions','Summaries')");
                table.CheckConstraint("ck_bill_pages_detail", "role <> 'Detail' OR (job_id = detail_job_id AND page_order = 0 AND closes_chain)");
                table.ForeignKey(
                    name: "FK_bill_pages_bill_gates_detail_job_id",
                    column: x => x.detail_job_id,
                    principalTable: "bill_gates",
                    principalColumn: "detail_job_id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_bill_pages_collection_jobs_job_id",
                    column: x => x.job_id,
                    principalTable: "collection_jobs",
                    principalColumn: "job_id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_bill_pages_document_extractions_extraction_id",
                    column: x => x.extraction_id,
                    principalTable: "document_extractions",
                    principalColumn: "extraction_id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "action_candidates",
            columns: table => new
            {
                candidate_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                source_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                congress = table.Column<int>(type: "integer", nullable: false),
                bill_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                bill_number = table.Column<int>(type: "integer", nullable: false),
                update_date_utc_ticks = table.Column<long>(type: "bigint", nullable: false),
                detail_job_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                configuration_revision_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                created_at_utc_ticks = table.Column<long>(type: "bigint", nullable: false),
                summaries_completed_at_utc_ticks = table.Column<long>(type: "bigint", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_action_candidates", x => x.candidate_id);
                table.CheckConstraint("ck_action_candidates_id", "candidate_id ~ '^[0-9a-f]{64}$'");
                table.CheckConstraint("ck_action_candidates_bill", "congress >= 1 AND bill_number >= 1");
                table.CheckConstraint("ck_action_candidates_update", "update_date_utc_ticks BETWEEN 0 AND 3155378975999999999");
                table.CheckConstraint("ck_action_candidates_created", "created_at_utc_ticks BETWEEN 0 AND 3155378975999999999");
                table.CheckConstraint("ck_action_candidates_summaries", "summaries_completed_at_utc_ticks IS NULL OR summaries_completed_at_utc_ticks BETWEEN 0 AND 3155378975999999999");
                table.ForeignKey(
                    name: "FK_action_candidates_bill_gates_detail_job_id",
                    column: x => x.detail_job_id,
                    principalTable: "bill_gates",
                    principalColumn: "detail_job_id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_action_candidates_configuration_revision",
                    column: x => x.configuration_revision_id,
                    principalTable: "collection_configuration_revisions",
                    principalColumn: "revision_id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_bill_pages_extraction_id",
            table: "bill_pages",
            column: "extraction_id");

        migrationBuilder.CreateIndex(
            name: "ux_bill_pages_chain_order",
            table: "bill_pages",
            columns: new[] { "detail_job_id", "role", "page_order" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_action_candidates_configuration_revision_id",
            table: "action_candidates",
            column: "configuration_revision_id");

        migrationBuilder.CreateIndex(
            name: "ux_action_candidates_detail_job",
            table: "action_candidates",
            column: "detail_job_id",
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "action_candidates");

        migrationBuilder.DropTable(name: "bill_pages");

        migrationBuilder.DropCheckConstraint(
            name: "ck_bill_gates_cosponsor_count",
            table: "bill_gates");

        migrationBuilder.DropColumn(
            name: "cosponsor_count",
            table: "bill_gates");
    }
}
