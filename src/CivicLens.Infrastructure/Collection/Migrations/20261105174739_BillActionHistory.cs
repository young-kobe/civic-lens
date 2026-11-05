using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations
{
    /// <inheritdoc />
    public partial class BillActionHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "bill_actions",
                columns: table => new
                {
                    detail_job_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    job_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    action_date = table.Column<DateOnly>(type: "date", nullable: false),
                    action_time = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    text = table.Column<string>(type: "text", nullable: false),
                    type = table.Column<string>(type: "text", nullable: true),
                    action_code = table.Column<string>(type: "text", nullable: true),
                    source_system_code = table.Column<int>(type: "integer", nullable: true),
                    source_system_name = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bill_actions", x => new { x.detail_job_id, x.position });
                    table.CheckConstraint("ck_bill_actions_position", "position >= 0");
                    table.CheckConstraint("ck_bill_actions_text", "btrim(text) <> ''");
                    table.ForeignKey(
                        name: "FK_bill_actions_bill_gates_detail_job_id",
                        column: x => x.detail_job_id,
                        principalTable: "bill_gates",
                        principalColumn: "detail_job_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_bill_actions_bill_pages_job_id",
                        column: x => x.job_id,
                        principalTable: "bill_pages",
                        principalColumn: "job_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "bill_action_recorded_votes",
                columns: table => new
                {
                    detail_job_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    action_position = table.Column<int>(type: "integer", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    chamber = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    congress = table.Column<int>(type: "integer", nullable: false),
                    session_number = table.Column<int>(type: "integer", nullable: false),
                    roll_number = table.Column<int>(type: "integer", nullable: false),
                    voted_at_utc_ticks = table.Column<long>(type: "bigint", nullable: true),
                    url = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bill_action_recorded_votes", x => new { x.detail_job_id, x.action_position, x.position });
                    table.CheckConstraint("ck_bill_action_recorded_votes_chamber", "chamber IN ('House', 'Senate')");
                    table.CheckConstraint("ck_bill_action_recorded_votes_position", "action_position >= 0 AND position >= 0");
                    table.CheckConstraint("ck_bill_action_recorded_votes_roll", "congress >= 1 AND session_number IN (1, 2) AND roll_number >= 1");
                    table.CheckConstraint("ck_bill_action_recorded_votes_url", "url IS NULL OR url LIKE 'https://%'");
                    table.CheckConstraint("ck_bill_action_recorded_votes_voted_at_utc_ticks", "voted_at_utc_ticks BETWEEN 0 AND 3155378975999999999");
                    table.ForeignKey(
                        name: "FK_bill_action_recorded_votes_bill_actions_detail_job_id_actio~",
                        columns: x => new { x.detail_job_id, x.action_position },
                        principalTable: "bill_actions",
                        principalColumns: new[] { "detail_job_id", "position" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_bill_actions_job_id",
                table: "bill_actions",
                column: "job_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bill_action_recorded_votes");

            migrationBuilder.DropTable(
                name: "bill_actions");
        }
    }
}
