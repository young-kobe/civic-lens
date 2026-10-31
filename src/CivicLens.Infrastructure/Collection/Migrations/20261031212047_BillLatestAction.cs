using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations
{
    /// <inheritdoc />
    public partial class BillLatestAction : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "latest_action_date",
                table: "bill_gates",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "latest_action_read",
                table: "bill_gates",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "latest_action_text",
                table: "bill_gates",
                type: "text",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_bill_gates_latest_action",
                table: "bill_gates",
                sql: "(latest_action_date IS NULL) = (latest_action_text IS NULL) AND (latest_action_text IS NULL OR latest_action_read AND char_length(latest_action_text) BETWEEN 1 AND 1000 AND latest_action_text = btrim(latest_action_text))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_bill_gates_latest_action",
                table: "bill_gates");

            migrationBuilder.DropColumn(
                name: "latest_action_date",
                table: "bill_gates");

            migrationBuilder.DropColumn(
                name: "latest_action_read",
                table: "bill_gates");

            migrationBuilder.DropColumn(
                name: "latest_action_text",
                table: "bill_gates");
        }
    }
}
