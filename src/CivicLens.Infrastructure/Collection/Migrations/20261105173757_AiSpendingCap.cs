using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations
{
    /// <inheritdoc />
    public partial class AiSpendingCap : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_vote_analysis_counts",
                table: "vote_analysis");

            migrationBuilder.DropCheckConstraint(
                name: "ck_statement_analysis_counts",
                table: "statement_analysis");

            migrationBuilder.DropCheckConstraint(
                name: "ck_relationship_analysis_counts",
                table: "relationship_analysis");

            migrationBuilder.DropCheckConstraint(
                name: "ck_document_change_analysis_counts",
                table: "document_change_analysis");

            migrationBuilder.DropCheckConstraint(
                name: "ck_brief_analysis_counts",
                table: "brief_analysis");

            migrationBuilder.DropCheckConstraint(
                name: "ck_action_analysis_counts",
                table: "action_analysis");

            migrationBuilder.AddColumn<long>(
                name: "reserved_micros",
                table: "vote_analysis",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "reserved_micros",
                table: "statement_analysis",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "reserved_micros",
                table: "relationship_analysis",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "reserved_micros",
                table: "document_change_analysis",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "reserved_micros",
                table: "brief_analysis",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "reserved_micros",
                table: "action_analysis",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateTable(
                name: "ai_month_spend",
                columns: table => new
                {
                    month_start = table.Column<DateOnly>(type: "date", nullable: false),
                    charged_micros = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_month_spend", x => x.month_start);
                    table.CheckConstraint("ck_ai_month_spend_charge", "charged_micros >= 0");
                    table.CheckConstraint("ck_ai_month_spend_month", "EXTRACT(DAY FROM month_start) = 1");
                });

            migrationBuilder.CreateTable(
                name: "ai_monthly_cap_revisions",
                columns: table => new
                {
                    revision_number = table.Column<int>(type: "integer", nullable: false),
                    cap_micros = table.Column<long>(type: "bigint", nullable: false),
                    author_subject = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    created_at_utc_ticks = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_monthly_cap_revisions", x => x.revision_number);
                    table.CheckConstraint("ck_ai_monthly_cap_revisions_cap", "cap_micros >= 0");
                    table.CheckConstraint("ck_ai_monthly_cap_revisions_created_at_utc_ticks", "created_at_utc_ticks BETWEEN 0 AND 3155378975999999999");
                    table.CheckConstraint("ck_ai_monthly_cap_revisions_number", "revision_number >= 1");
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_vote_analysis_counts",
                table: "vote_analysis",
                sql: "fence >= 0 AND attempts >= 0 AND reserved_tokens >= 0 AND reserved_micros >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_vote_analysis_reserved_micros",
                table: "vote_analysis",
                sql: "reserved_tokens > 0 OR reserved_micros = 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_statement_analysis_counts",
                table: "statement_analysis",
                sql: "fence >= 0 AND attempts >= 0 AND reserved_tokens >= 0 AND reserved_micros >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_statement_analysis_reserved_micros",
                table: "statement_analysis",
                sql: "reserved_tokens > 0 OR reserved_micros = 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_relationship_analysis_counts",
                table: "relationship_analysis",
                sql: "fence >= 0 AND attempts >= 0 AND reserved_tokens >= 0 AND reserved_micros >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_relationship_analysis_reserved_micros",
                table: "relationship_analysis",
                sql: "reserved_tokens > 0 OR reserved_micros = 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_document_change_analysis_counts",
                table: "document_change_analysis",
                sql: "fence >= 0 AND attempts >= 0 AND reserved_tokens >= 0 AND reserved_micros >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_document_change_analysis_reserved_micros",
                table: "document_change_analysis",
                sql: "reserved_tokens > 0 OR reserved_micros = 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_brief_analysis_counts",
                table: "brief_analysis",
                sql: "fence >= 0 AND attempts >= 0 AND reserved_tokens >= 0 AND reserved_micros >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_brief_analysis_reserved_micros",
                table: "brief_analysis",
                sql: "reserved_tokens > 0 OR reserved_micros = 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_action_analysis_counts",
                table: "action_analysis",
                sql: "fence >= 0 AND attempts >= 0 AND reserved_tokens >= 0 AND reserved_micros >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_action_analysis_reserved_micros",
                table: "action_analysis",
                sql: "reserved_tokens > 0 OR reserved_micros = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ai_month_spend");

            migrationBuilder.DropTable(
                name: "ai_monthly_cap_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_vote_analysis_counts",
                table: "vote_analysis");

            migrationBuilder.DropCheckConstraint(
                name: "ck_vote_analysis_reserved_micros",
                table: "vote_analysis");

            migrationBuilder.DropCheckConstraint(
                name: "ck_statement_analysis_counts",
                table: "statement_analysis");

            migrationBuilder.DropCheckConstraint(
                name: "ck_statement_analysis_reserved_micros",
                table: "statement_analysis");

            migrationBuilder.DropCheckConstraint(
                name: "ck_relationship_analysis_counts",
                table: "relationship_analysis");

            migrationBuilder.DropCheckConstraint(
                name: "ck_relationship_analysis_reserved_micros",
                table: "relationship_analysis");

            migrationBuilder.DropCheckConstraint(
                name: "ck_document_change_analysis_counts",
                table: "document_change_analysis");

            migrationBuilder.DropCheckConstraint(
                name: "ck_document_change_analysis_reserved_micros",
                table: "document_change_analysis");

            migrationBuilder.DropCheckConstraint(
                name: "ck_brief_analysis_counts",
                table: "brief_analysis");

            migrationBuilder.DropCheckConstraint(
                name: "ck_brief_analysis_reserved_micros",
                table: "brief_analysis");

            migrationBuilder.DropCheckConstraint(
                name: "ck_action_analysis_counts",
                table: "action_analysis");

            migrationBuilder.DropCheckConstraint(
                name: "ck_action_analysis_reserved_micros",
                table: "action_analysis");

            migrationBuilder.DropColumn(
                name: "reserved_micros",
                table: "vote_analysis");

            migrationBuilder.DropColumn(
                name: "reserved_micros",
                table: "statement_analysis");

            migrationBuilder.DropColumn(
                name: "reserved_micros",
                table: "relationship_analysis");

            migrationBuilder.DropColumn(
                name: "reserved_micros",
                table: "document_change_analysis");

            migrationBuilder.DropColumn(
                name: "reserved_micros",
                table: "brief_analysis");

            migrationBuilder.DropColumn(
                name: "reserved_micros",
                table: "action_analysis");

            migrationBuilder.AddCheckConstraint(
                name: "ck_vote_analysis_counts",
                table: "vote_analysis",
                sql: "fence >= 0 AND attempts >= 0 AND reserved_tokens >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_statement_analysis_counts",
                table: "statement_analysis",
                sql: "fence >= 0 AND attempts >= 0 AND reserved_tokens >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_relationship_analysis_counts",
                table: "relationship_analysis",
                sql: "fence >= 0 AND attempts >= 0 AND reserved_tokens >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_document_change_analysis_counts",
                table: "document_change_analysis",
                sql: "fence >= 0 AND attempts >= 0 AND reserved_tokens >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_brief_analysis_counts",
                table: "brief_analysis",
                sql: "fence >= 0 AND attempts >= 0 AND reserved_tokens >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_action_analysis_counts",
                table: "action_analysis",
                sql: "fence >= 0 AND attempts >= 0 AND reserved_tokens >= 0");
        }
    }
}
