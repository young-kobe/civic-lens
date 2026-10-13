using CivicLens.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations;

[DbContext(typeof(CollectionAttemptDbContext))]
[Migration("20261016120000_BillGates")]
public partial class BillGates : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "bill_gates",
            columns: table => new
            {
                detail_job_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                source_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                congress = table.Column<int>(type: "integer", nullable: false),
                bill_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                bill_number = table.Column<int>(type: "integer", nullable: false),
                update_date_utc_ticks = table.Column<long>(type: "bigint", nullable: false),
                policy_area = table.Column<string>(type: "text", nullable: true),
                issue_revision_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                decision = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                reason = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                created_at_utc_ticks = table.Column<long>(type: "bigint", nullable: false),
                decided_at_utc_ticks = table.Column<long>(type: "bigint", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_bill_gates", x => x.detail_job_id);
                table.CheckConstraint("ck_bill_gates_bill", "congress >= 1 AND bill_number >= 1");
                table.CheckConstraint("ck_bill_gates_update", "update_date_utc_ticks BETWEEN 0 AND 3155378975999999999");
                table.CheckConstraint("ck_bill_gates_created", "created_at_utc_ticks BETWEEN 0 AND 3155378975999999999");
                table.CheckConstraint("ck_bill_gates_decided", "(decision = 'Pending') = (decided_at_utc_ticks IS NULL) AND (decided_at_utc_ticks IS NULL OR decided_at_utc_ticks BETWEEN 0 AND 3155378975999999999)");
                table.CheckConstraint("ck_bill_gates_issue_revision", "issue_revision_id ~ '^[0-9a-f]{64}$'");
                table.CheckConstraint("ck_bill_gates_reason", "(decision = 'Pending' AND reason IS NULL) OR (decision = 'Passed' AND reason IS NOT NULL AND reason IN ('CoveredSponsor','TrackedPolicyArea','CoveredCosponsor')) OR (decision = 'NotRelevant' AND reason IS NOT NULL AND reason IN ('NoPolicyArea','UntrackedPolicyArea'))");
                table.ForeignKey(
                    name: "FK_bill_gates_collection_jobs_detail_job_id",
                    column: x => x.detail_job_id,
                    principalTable: "collection_jobs",
                    principalColumn: "job_id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "bill_follow_on_jobs",
            columns: table => new
            {
                job_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                detail_job_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_bill_follow_on_jobs", x => x.job_id);
                table.ForeignKey(
                    name: "FK_bill_follow_on_jobs_bill_gates_detail_job_id",
                    column: x => x.detail_job_id,
                    principalTable: "bill_gates",
                    principalColumn: "detail_job_id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_bill_follow_on_jobs_collection_jobs_job_id",
                    column: x => x.job_id,
                    principalTable: "collection_jobs",
                    principalColumn: "job_id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "ux_bill_gates_bill_version",
            table: "bill_gates",
            columns: new[] { "source_id", "congress", "bill_type", "bill_number", "update_date_utc_ticks" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_bill_follow_on_jobs_detail_job_id",
            table: "bill_follow_on_jobs",
            column: "detail_job_id");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "bill_follow_on_jobs");

        migrationBuilder.DropTable(name: "bill_gates");
    }
}
