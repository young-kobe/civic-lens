using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations
{
    /// <inheritdoc />
    public partial class ActionRevisionDerivedColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "cosponsor_count",
                table: "action_revisions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "event_on",
                table: "action_revisions",
                type: "date",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_action_revisions_event_on",
                table: "action_revisions",
                column: "event_on");

            migrationBuilder.AddCheckConstraint(
                name: "ck_action_revisions_cosponsor_count",
                table: "action_revisions",
                sql: "cosponsor_count >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_action_revisions_derived_columns_written",
                table: "action_revisions",
                sql: "num_nulls(event_on, cosponsor_count) IN (0, 2)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_action_revisions_event_on",
                table: "action_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_action_revisions_cosponsor_count",
                table: "action_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_action_revisions_derived_columns_written",
                table: "action_revisions");

            migrationBuilder.DropColumn(
                name: "cosponsor_count",
                table: "action_revisions");

            migrationBuilder.DropColumn(
                name: "event_on",
                table: "action_revisions");
        }
    }
}
